# Leaf entities: archive and delete — implementation tasks

Companion to [entity_life_cycle_design.md](entity_life_cycle_design.md). That
document decides *what* we build. This one lists *how*, task by task, for the
leaf entities only.

The protocol both documents follow lives in the `entity-lifecycle` skill at
`.claude/skills/entity-lifecycle/`.

Audience: whoever picks up the work. Each task says what to do and what to watch
out for.

## What a leaf entity is

A leaf is a row that nothing else points at.

Delete a leaf and nothing is left dangling, so we delete it on the spot — one
write, no background job, no "Deleting" state. That is the whole reason to do
the leaves first: they are the part of deletion we can ship today.

## Which entities are leaves

| Entity                  | Leaf? | Why                                                                                                                 |
|-------------------------|-------|---------------------------------------------------------------------------------------------------------------------|
| Activity                | Yes   | Runs keep their own copy (a snapshot). `ActivityResult.ActivityId` resolves against that snapshot, not the live row |
| EnvironmentVariable     | Yes   | Runs snapshot the values too                                                                                        |
| Organization membership | Yes   | Nothing references a membership row                                                                                 |

And the near misses, so nobody adds them to this list by mistake:

| Entity                       | Leaf? | Why not                                                            |
|------------------------------|-------|--------------------------------------------------------------------|
| Precondition                 | No    | Activities hold `PreconditionIds` pointing at it                   |
| EvidenceDefinition           | No    | Activities hold `EvidenceIds` pointing at it                       |
| Environment                  | No    | Owns EnvironmentVariable rows                                      |
| Scenario                     | No    | Owns Activity rows                                                 |
| Application                  | No    | Owns Scenarios, Environments, libraries, Runs                      |
| Run                          | No    | Owns RunStatusUpdate and RunningRun rows                           |
| RunStatusUpdate / RunningRun | n/a   | Parts of a Run. They have no API of their own and die with the Run |
| User, Refresh token          | n/a   | Out of scope. Neither is deletable today                           |

Those four parent entities need the "mark as Deleting, then a worker clears the
children" protocol. That is separate work and is not covered here.

## Archive does not apply to leaves

The design document marks Archive as "no" for all three leaves. That is on
purpose, and it matches how people use them.

Archiving exists so a user can retire something without losing history — an old
Application, a Scenario nobody runs any more. A leaf has no history worth
keeping on its own. An Activity only means something inside its Scenario. A
variable only means something inside its Environment. A membership is either
there or it is not; a half-member has no useful meaning. On top of that,
archiving costs real work: a `LifecycleState` field, a filter on every list, a
second "list archived" endpoint, and an unarchive endpoint. We would pay all of
it for a state nobody asks for.

So: **no `LifecycleState` field on these three entities.** Delete means the row
is gone.

Nam signed this off, and the `entity-lifecycle` skill now says so. Its
"Entities With No Lifecycle State" section names these three and explains the
rule: a leaf gets the field only when a user needs to archive it. Nobody needs
to reopen the question here.

Past runs still show the old values, because a Run copies what it used at start
time. Deleting a variable does not rewrite yesterday's Run.

## Rules that apply to all three

Read these once; every task below assumes them.

**Naming.** Use `DeleteAsync` when the call cannot meaningfully fail, and
`TryDeleteAsync` returning `bool` when it can — same convention as
`TrySaveAsync`
and `TryUpdateAsync` today.

**Tenant scoping.** `OrganizationId` goes into every key and every condition. No
exceptions. A delete that forgets the tenant is a cross-customer data loss bug,
which is the worst kind we can ship.

**Deleting twice.** DynamoDB's `DeleteItem` succeeds even when the row was
already gone, so the storage layer never minds a repeat. The API answer depends
on what the URL points at, and there are two shapes here:

- **The row is the URL resource** — `DELETE /activities/{activityId}`. The
  endpoint looks the row up first, so a second call finds nothing and returns
  **404**, exactly like `UpdateActivity` does today. Keep it that way; inventing
  a 204 here would make delete the only endpoint in the file that treats an
  unknown id as success.
- **The row hangs off a parent in the URL** — `DELETE
  /environments/{environmentId}/variables/{key}`. An unknown Environment is
  **404**. A known Environment with no such variable is **204**: the end state
  the caller asked for is already true, and we never looked the variable up.
  Membership removal follows the same shape — the Organization is the parent, so
  a user who is not a member gets 204.

Write the rule into each endpoint's XML doc so the frontend can rely on it.

**No worker queue.** None of this needs the background deletion queue. Do not
build one here.

**Archived Organizations block all three.** `RequireActiveOrganizationFilter` is
a global filter that rejects every non-GET request with 403 when the caller's
Organization is not Active, unless the endpoint carries
`[AllowArchivedOrganization]`. All three new endpoints are DELETEs, so all three
inherit that 403 and none of them should carry the attribute — you cannot edit
an archived Organization's contents, and deleting is editing. Two follow-ons:
the OpenAPI 403 is added automatically by
`ArchivedOrganizationOperationTransformer`, so do not hand-write it; and each
section's tests need one case proving the DELETE returns 403 inside an archived
Organization.

**Tests.** Follow the `testing-guideline` skill: integration tests in
`A2.Server.Tests` against DynamoDB Local, with all four types — happy path, bad
data, invalid shape, and unauthorized access.

**SDK and web.** Every new endpoint changes the API surface, so
`../scripts/generate-sdk.sh` has to run, and `autoassure-web` needs the delete
button wired up. Ask Nam before running the SDK script (project rule).

---

## Section 1 — Activity

An Activity is a step inside a Scenario. Deleting one is the most interesting of
the three, because the Scenario keeps a counter of how many Activities it has.

Files (all under `src/A2.Server/`): `Repositories/IActivityRepository.cs`,
`Repositories/DynamoDbActivityRepository.cs`,
`Controllers/ActivitiesController.cs`.

### Task 1.1 — Add `TryDeleteAsync` to the Activity repository

Add one method to `IActivityRepository` and implement it:

```csharp
Task<bool> TryDeleteAsync(
    Guid organizationId,
    Guid applicationId,
    Guid scenarioId,
    Guid activityId
);
```

`applicationId` is not optional. The Scenario table is keyed by
`OrganizationId_ApplicationId` plus `Id`, so without it you cannot even name the
Scenario row you need to update. `TryUpdateAsync` already takes it for exactly
this reason, and its XML doc explains where the caller gets it — copy that
wording.

It writes a DynamoDB transaction with two items: a `Delete` on the Activity row,
and an `Update` on the Scenario row that does `ADD ActivityCount :minusOne`.
Mirror `IncrementScenarioActivityCount` in the same file — the decrement is its
twin and belongs next to it.

**Watch out — the counter is the whole reason this is a transaction.** Three
things have to be right.

*The Activity delete needs its own condition.* Put
`ConditionExpression = "attribute_exists(Id)"` on the `Delete`, not just on the
Scenario update. Without it, deleting an already-deleted Activity still succeeds
and still subtracts one, and the Scenario's count drifts below the real number —
which eventually lets a user exceed `MaxActivityCountPerScenario`.

*The Scenario update needs a floor.* `attribute_exists(Id)` alone is not enough.
Scenario rows written before `ActivityCount` existed have no such attribute, and
`ADD ActivityCount :minusOne` on a missing attribute happily creates it at `-1`.
The increment side already guards this case with
`attribute_not_exists(ActivityCount) OR ActivityCount < :max`; the decrement
needs the mirror image:

```
attribute_exists(Id) AND ActivityCount > :zero
```

`ActivityCount > :zero` is false when the attribute is missing, which is the
behaviour we want — a legacy row's counter stays absent rather than going
negative.

*The two failures are not the same failure.* Catch
`TransactionCanceledException` inside this method — do not reuse a shared
handler, per the coding standards — and read `ex.CancellationReasons` by index,
the way `TrySaveAsync` and `TryUpdateAsync` already do. Reason at the Activity's
index means the Activity was already gone: return `false`. Reason at the
Scenario's index means a vanished Scenario or a counter already at zero, which
is a broken invariant, not a normal outcome: rethrow so it shows up in the logs
instead of being swallowed as an ordinary 404.

### Task 1.2 — Accept the gap in `Order`

Do nothing to the remaining Activities' `Order` values. Write a test that proves
a gap is harmless.

**Watch out.** `Order` is 0-based — `Create` assigns `scenario.ActivityCount`
and `TryReorderAsync` assigns the list index, both starting at zero. So three
steps are 0, 1, 2, and deleting the middle one leaves 0 and 2. That is fine:
`ListByScenarioAsync` sorts by `Order`, and it never assumes the numbers are
contiguous. Assert the exact values 0 and 2 in the test, not "there is a gap".
Renumbering is tempting and wrong — it would mean rewriting every sibling row
inside the same transaction, which caps us at 100 items and turns a one-row
delete into a Scenario-wide write. The user already has `TryReorderAsync` when
they actually want to renumber. Say this out loud in a test name so the next
person does not "fix" it.

### Task 1.3 — Add `DELETE /activities/{activityId}`

Add the endpoint to `ActivitiesController`, named `DeleteActivity`, returning
204 on success and 404 when the Activity is not in the caller's Organization.
The flow: resolve the caller's Organization, call `GetByIdAsync(organizationId,
activityId)` to learn the Activity's `ApplicationId` and `ScenarioId`, then pass
both into `TryDeleteAsync`. `Update` in the same controller already does this
lookup-then-write dance — follow it line for line, including returning 404 when
`TryDeleteAsync` comes back `false`.

The route carries only `activityId`, matching the existing `UpdateActivity`
route, while the storage key needs the Scenario. The lookup is how we get it.
Do not add `scenarioId` to the URL; keep the API shaped the way the frontend
already uses it. A rapid double delete may reach `TryDeleteAsync` twice — the
condition from Task 1.1 is what keeps the counter honest.

### Task 1.4 — Leave running Runs alone

No code. Confirm with a test that deleting an Activity does not touch any Run.

**Watch out.** A Run copies the Activities it needs when it starts, via
`RunSnapshotBuilder`. Deleting the live Activity afterwards changes nothing in
that Run, and that is exactly the behaviour we want: a Run is a record of what
happened, not a live view. Do not add a "is this Activity in a running Run?"
check. It would be slow, racy, and would block a legitimate edit.

### Task 1.5 — Tests

In `tests/A2.Server.Tests/Repositories/DynamoDbActivityRepositoryTests.cs` and
`tests/A2.Server.Tests/Controllers/ActivitiesControllerTests.cs`:

Repository tests (`DynamoDbActivityRepositoryTests`):

- Deleting an Activity removes it and drops the Scenario's `ActivityCount` by
  one.
- Calling `TryDeleteAsync` twice returns `false` the second time and leaves
  `ActivityCount` unchanged.
- Deleting one Activity leaves the others, gaps in `Order` included (0 and 2).
- A Scenario row with no `ActivityCount` attribute at all: the delete fails and
  the attribute is still absent, not `-1`.

Controller tests (`ActivitiesControllerTests`):

- Deleting an Activity returns 204 and the Scenario's count drops by one.
- Deleting the same Activity twice returns 404 the second time.
- An Activity in another Organization returns 404 and stays put.
- No token returns 401.
- The caller's Organization is archived: 403, and the Activity stays put.

**Watch out.** Put the double-delete test in the *repository* file, not the
controller file. At the controller level the second call never reaches
`TryDeleteAsync` — `GetByIdAsync` returns null first and the request ends as a
404 — so a controller-level test proves nothing about the condition expression
it is supposed to be testing. The counter tests matter more than the "row is
gone" tests either way: read the Scenario back and assert the number, rather
than trusting the delete's return value.

---

## Section 2 — Environment variable

A variable is one key-value pair inside an Environment. The write path already
exists: `PUT /environments/{environmentId}/variables/{key}`.

Files (all under `src/A2.Server/`):
`Repositories/IEnvironmentVariableRepository.cs`,
`Repositories/DynamoDbEnvironmentVariableRepository.cs`,
`Controllers/EnvironmentsController.cs`.

### Task 2.1 — Add `DeleteAsync` to the variable repository

Add:

```csharp
Task DeleteAsync(Guid organizationId, Guid environmentId, string variableKey);
```

One `DeleteItem` call on the partition key `{organizationId}_{environmentId}`
with sort key `Key`. No transaction, no condition.

**Watch out — two things.**

*Do not copy the transaction from `TrySaveAsync`.* That method uses one because
it must check the Environment row exists before writing. The check stops
somebody creating a variable under an Environment that is not there, which would
leave a row nobody can reach. Deleting has no such risk: if the Environment is
already gone, removing a variable from it breaks nothing. One `DeleteItem` call
is enough.

*Build the partition key by calling `GetPartitionKey`.* Do not write the format
string out again. Get that string wrong and the delete aims at a partition that
does not exist — DynamoDB reports success and the variable is still sitting
there. So the test has to read the variable back and prove it is gone. "The call
did not throw" proves nothing here.

### Task 2.2 — Add `DELETE /environments/{environmentId}/variables/{key}`

Add it to `EnvironmentsController`, named `DeleteEnvironmentVariable`. Copy the
`key` validation from `SetVariable` exactly: max 200 characters, regex
`^[A-Za-z0-9_]+$`. Look up the Environment first and return 404 when it is
missing; otherwise delete and return 204.

**Watch out.** Two different "not found" cases hide in one URL, and the caller
needs to tell them apart. An unknown Environment is a 404 — the user is pointing
at something that does not exist. An unknown variable inside a real Environment
is a 204 — the end state they asked for is already true. Keep the validation
attributes on `key` even though this is a delete; without them a request with a
3,000-character key reaches DynamoDB and fails with an error nobody can read.

### Task 2.3 — Keep secret values out of the response

Return 204 with no body.

**Watch out.** A variable can be marked `IsSensitive`, and the API masks those
on the way out. The easy mistake is returning the deleted variable "for
convenience", which hands back the secret in plain text on its way to the
browser console and the server logs. Nothing to return here. Return nothing.

### Task 2.4 — Tests

In `DynamoDbEnvironmentVariableRepositoryTests.cs` and
`EnvironmentsControllerTests.cs`:

- Deleting one variable leaves the others in the Environment untouched.
- Deleting a variable that is not there returns 204.
- Deleting from an unknown Environment returns 404.
- A variable in another Organization's Environment returns 404 and is not
  deleted.
- An over-long key, or a key with a space, returns 400.
- No token returns 401.
- The caller's Organization is archived: 403, and the variable stays put.

**Watch out.** The "leaves the others untouched" test is the important one. The
whole point of storing one row per variable is that touching one never rewrites
the rest.

---

## Section 3 — Organization membership

A membership links a User to an Organization with a Role (Owner or Member).
Deleting one removes a person from a customer's account. This is the riskiest of
the three, and it is not risky because of the database.

Files (all under `src/A2.Server/`):
`Repositories/IOrganizationUserRepository.cs`,
`Repositories/DynamoDbOrganizationUserRepository.cs`,
`Controllers/OrganizationsController.cs`.

### Task 3.1 — Put `OwnerCount` on the Organization row

Add one field to `Organization` (`Models/Organization.cs`):

```csharp
public required int OwnerCount { get; init; }
```

Map it in `DynamoDbMapper.Organization.cs` both ways, next to `IsPersonal`.

This number is what stops a customer locking themselves out. Every write that
changes how many Owners an Organization has must change this number in the same
DynamoDB transaction — never in a second call. Four write paths touch it:

| Write path                         | Effect on `OwnerCount`                       |
|------------------------------------|----------------------------------------------|
| Personal Organization created      | Set to `1`                                   |
| Membership added as Owner          | `ADD OwnerCount :one`                        |
| Member promoted to Owner           | `ADD OwnerCount :one`                        |
| Owner demoted, or Owner removed    | `ADD OwnerCount :minusOne`, only if `> 1`    |

Concretely, three methods change in this task, and the delete in Task 3.2 is the
fourth:

- `TryCreatePersonalOrganizationAsync` (`DynamoDbUserRepository.cs`) already
  writes the Organization and its Owner membership in one transaction. Set
  `OwnerCount = 1` on the Organization it builds in
  `GoogleUserSyncService.CreatePersonalOrganizationAsync`.
- `TryCreateAsync` (`DynamoDbOrganizationUserRepository.cs`) becomes a
  transaction: the membership `Put` as today, plus `ADD OwnerCount :one` on the
  Organization row when the new membership's `Role` is `Owner`. A Member
  changes nothing.
- `TryUpdateAsync` in the same file becomes a transaction: the membership
  update, plus `ADD OwnerCount :one` when promoting and `ADD OwnerCount
  :minusOne` when demoting, the demotion carrying the same
  `OwnerCount > :one` guard as the delete. It needs the caller's expected
  *current* role as an argument, and a `#role = :expectedRole` condition on the
  update — same shape as `DeleteAsync`. Without it, setting an existing Owner to
  Owner again counts a promotion that never happened, and the counter climbs
  above the truth.

**Watch out — do `TryCreateAsync` now, even though nothing calls it.** Only
`TryCreatePersonalOrganizationAsync` creates memberships today; `TryCreateAsync`
is waiting for an invite API that does not exist yet. Leave the increment out
and whoever builds invites has to discover a counter they have never heard of.
A counter that reads low is worse than no counter at all — it refuses removals
that should be allowed, and the error message blames the wrong thing.

**Watch out — demotion is as dangerous as removal**, and is the easier one to
forget. The last Owner demoting themselves to Member leaves an Organization
nobody can administer: same lockout, different door.

### Task 3.2 — Add the delete to the membership repository

Add:

```csharp
Task<RemoveMembershipOutcome> DeleteAsync(
    Guid organizationId,
    Guid userId,
    OrganizationRole role
);
```

```csharp
public enum RemoveMembershipOutcome
{
    Removed,
    NotFound,
    CannotDeleteLastOwner,
}
```

It writes a DynamoDB transaction with two items:

1. `Delete` on the membership row, condition
   `attribute_exists(OrganizationId) AND attribute_exists(UserId) AND #role = :role`.
2. `Update` on the Organization row, `ADD OwnerCount :minusOne`, condition
   `attribute_exists(Id) AND OwnerCount > :one`. **Include this item only when
   `role` is `Owner`.** Removing a Member changes no counter, so that call is a
   single-item transaction.

Catch `TransactionCanceledException` in this method and read
`ex.CancellationReasons` by index, the way `TrySaveAsync` does elsewhere. Reason
at the membership's index means no such row, or the row is no longer the role
the caller expected: return `NotFound`. Reason at the Organization's index means
the counter is already at `1`: return `CannotDeleteLastOwner`.

**Why `role` is a parameter.** Not as a permission check — the repository
decides nothing about who may remove whom. It is there because DynamoDB cannot
read the membership row's `Role` to decide whether the decrement applies. The
caller has to say. Conditioning the delete on that same value is then free, and
it keeps the counter honest: if someone was promoted between the controller's
read and this write, the delete fails instead of decrementing for a row that was
never an Owner.

**Why not `TryDeleteAsync` returning `bool`.** Three outcomes, three answers
from the endpoint: 204, 404, and 400. A bool cannot carry that, and a controller
that has to guess which failure it got will guess wrong.

The controller also needs the target's current role before it can call this, so
add the single-row read too:

```csharp
Task<OrganizationUser?> GetAsync(Guid organizationId, Guid userId);
```

A `GetItem` on the base table's two keys. Not a query over the whole
Organization — we want one row, and the transaction handles the counting.

**Watch out.** Keep these methods dumb in every other respect. Who may remove
whom, and whether personal Organizations are exempt, belongs in the controller —
repositories hold no business logic (coding standards), and rules buried in a
repository are rules nobody finds later.

### Task 3.3 — Add the remove-member endpoint and its rules

Add `DELETE /organizations/{organizationId}/members/{userId}` to
`OrganizationsController`, named `RemoveOrganizationMember`. The controller
already carries `[Route("organizations")]`, so the attribute is
`[HttpDelete("{organizationId:guid}/members/{userId:guid}", Name =
"RemoveOrganizationMember")]` — do not repeat the prefix. Enforce, in order:

1. The target Organization is the caller's own Organization, else 400 — same
   shape as `Archive` today.
2. The caller is an Owner, else 403. A Member may not remove anyone, and that
   includes themselves — see Decisions.
3. The Organization is not personal (`IsPersonal`), else 400. A personal
   Organization has exactly one member and must keep them.
4. Read the target's membership to get their `Role`. No membership, 404.
5. Call `DeleteAsync` with that role and answer on the outcome: `Removed` is
   204, `NotFound` is 404, `CannotDeleteLastOwner` is 400.

**Watch out.** Rules 3 and 5 are what protect customers from locking themselves
out. Let the last Owner go and the Organization still holds data but has nobody
who can administer it — unrecoverable without a support engineer touching the
database.

The last-owner check lives in the `OwnerCount` condition, not in a count the
controller reads first. That is deliberate. Two Owners removing each other at
the same instant both read "two Owners, fine", and a controller-side check lets
both deletes through. Both writes hit the same Organization row, so the
condition lets exactly one win. Do not add a friendly pre-check on top "for a
better message" — it will disagree with the write under load, which is worse
than one extra round trip.

Write the error messages for a human: "You are the only owner. Make someone else
an owner before leaving." Each rule needs its own XML doc line on the endpoint,
saying exactly what triggers the 400 or the 403, so the frontend can prevent
it.

### Task 3.4 — Leave the removed user's traces alone

No code. Add a test that proves the removed user's data survives.

**Watch out.** Every row in the system carries `CreatedByUserId` and
`UpdatedByUserId`. Removing a membership must not touch them, and must not touch
the User row either. A Scenario written by someone who has left still says who
wrote it — that is an audit trail, and deleting it would be data loss dressed up
as cleanup. The user simply loses access. Their name still resolves, because the
User table is separate from the membership table.

### Task 3.5 — Tests

In `DynamoDbOrganizationUserRepositoryTests.cs` and
`OrganizationsControllerTests.cs`:

- An Owner removes a Member: 204, and the member's `ListByUserAsync` no longer
  shows that Organization.
- A Member tries to remove someone: 403.
- A Member tries to remove themselves: 403, and the membership is still there.
- Removing the last Owner: 400, and the membership is still there afterwards.
- An Owner removes another Owner: 204, and the Organization's `OwnerCount` drops
  to 1. Read the Organization back and assert the number — the return value of
  the delete does not prove the counter moved.
- Removing a Member leaves `OwnerCount` untouched.
- Promoting a Member raises `OwnerCount`; demoting the last Owner is refused.
- Removing anyone from a personal Organization: 400, and the membership
  survives. Covers a user trying to remove themselves, which is the only way to
  reach zero Organizations.
- Removing a user from an Organization the caller does not belong to: 400, and
  nothing is deleted.
- Scenarios created by the removed user are still readable and still name them.
- No token returns 401.
- The caller's Organization is archived: 403, and the membership stays put.

**Watch out.** The last-Owner test and the personal-Organization test are the
two that justify this section. Do not let them be the ones cut for time.

---

## Suggested order of work

1. **Environment variable** — the simplest. One method, one endpoint. Use it to
   settle the 204-versus-404 convention for everyone else.
2. **Activity** — adds the counter transaction.
3. **Organization membership** — adds `OwnerCount` and the permission rules.
   Land `OwnerCount` and its four write paths first; the endpoint is easy once
   the counter is trustworthy.

Then regenerate the SDK once, after all three, and wire the web UI up.

None of this depends on the parent-entity work, and the parent work will reuse
all three delete methods when it clears a subtree. Doing the leaves first is not
just easier — it is the foundation.

## Decisions

Nam settled both open questions on 2026-09-20. Keep it simple: **only an Owner
adds or removes members.**

1. **No member self-removal.** A Member cannot leave on their own. An Owner
   removes them. So rule 2 in Task 3.3 is a plain Owner check, with no
   self-removal exception, and a Member asking to remove themselves gets the
   same 403 as a Member asking to remove anyone else. An Owner may still remove
   themselves, as long as they are not the last one — that is what the
   `OwnerCount` guard is for.

2. **Ship remove-member now. Do not expose an add-member API yet.** The only
   thing that creates a membership today stays the sign-up flow that builds a
   personal Organization. `TryCreateAsync` keeps no endpoint in front of it.
   Task 3.1 still puts the `OwnerCount` increment inside `TryCreateAsync`, for
   the reason that task already gives: whoever ships adding members later must
   not have to find a counter nobody told them about.

   So the API really is half a feature for now, on purpose. Removal is the half
   that protects customers — a person leaves the company and someone has to cut
   their access today. Adding people can wait for a proper flow.

What this leaves for later: adding a member, promoting and demoting through an
API, and whatever invite flow we choose. None of it is in this document.

