# Goal — Archiving an Organization

The reasoning behind this work lives in [entity_life_cycle_design.md](./entity_life_cycle_design.md). That document
explains why. This document holds only the work.

## The problems

1. **An Organization cannot be switched off.** A trial ends, a customer leaves, a workspace gets replaced. Right now
   the only two states are fully live and gone, and the design rules out ever deleting an Organization. So there is no
   way to retire one. Dead tenants sit in the product looking exactly like live ones.

2. **A retired tenant would still accept new work.** Hiding an Organization is not enough on its own. Without a check
   somewhere, an archived Organization keeps taking new Applications, Scenarios, Environments and Runs. Archiving
   would be decoration.

3. **A new cross-cutting response would rot the API docs.** The block returns `403`. There are 32 endpoints today.
   Hand-writing the `403` on each one means the 33rd endpoint, written months from now by someone who never read this
   document, is silently undocumented. The generated TypeScript SDK inherits the gap.

## The solutions

1. **One shared `LifecycleState` on the Organization.** The same enum every first-class entity uses — `Active`,
   `Archived`, `Deleting`. An Organization only ever holds the first two, because it is never purged.

2. **One filter at the front door, not a check in every repository.** The per-write parent check exists to stop rows
   dangling after a hard delete. An Organization is never hard-deleted, so nothing can dangle, so that expensive
   machinery buys nothing here. It would also turn every write in the product into a two-table transaction, which
   DynamoDB charges at double rate, forever, to guard a flag that flips once in a tenant's life. One check per request
   is enough.

3. **Archive and unarchive endpoints, Owner only, plus a list of archived Organizations.** Without the archived list,
   archiving is a one-way trip and nobody can undo it.

4. **An OpenAPI transformer documents the `403` everywhere, automatically.** It appends to an existing `403`
   description rather than overwriting it, because some endpoints already return `403` for their own reasons.

## Task breakdown

Four tasks, run in order. They are not independent and must not run in parallel: Task 2 needs the enum from Task 1,
Task 3 needs the filter from Task 2, and Task 4 needs the marker attribute from Task 3. Each task leaves the build
green and the tests passing.

Run every command from `/Users/namduong/Documents/autoassure/autoassure-server`.

| Checklist box | Command |
|---|---|
| Linting pass | `dotnet format A2.Server.slnx --verify-no-changes` then `dotnet jb inspectcode A2.Server.slnx -o=inspect.sarif.json --no-build --severity=WARNING` |
| Build pass | `dotnet build A2.Server.slnx` |
| Tests pass | `dotnet test tests/A2.Server.UnitTests/A2.Server.UnitTests.csproj` then `dotnet test tests/A2.Server.Tests/A2.Server.Tests.csproj` |

The integration tests start a real DynamoDB Local process. The first run downloads a jar to `~/.dynamodb-local` and
needs network access and Java on the path.

### Task 1 — Put `LifecycleState` on the Organization

**Do.** Add a `LifecycleState` enum with three values: `Active`, `Archived`, `Deleting`. It is shared — every
first-class entity will use it, so put it in `Models/` on its own, not nested inside `Organization`. Add a
`LifecycleState` property to the `Organization` model. An Organization only ever holds `Active` or `Archived`; it is
never purged, so `Deleting` does not apply to it. Nothing in the type enforces that, so say it in the XML doc. In the
mapper, write the attribute on save, and read a missing attribute as `Active` so rows written before this change still
load.

**Files.**

- `src/A2.Server/Models/LifecycleState.cs` (new)
- `src/A2.Server/Models/Organization.cs`
- `src/A2.Server/Repositories/DynamoDbMapper.Organization.cs`

**Tests.** In `tests/A2.Server.UnitTests`: a stored row round-trips its state; a row with no `LifecycleState`
attribute reads back as `Active`; a row with an unrecognised value fails loudly rather than defaulting silently.

- [x] Linting pass.
- [x] Build pass.
- [x] Tests pass
- [x] Output code reviewed with a seperate agent in a fresh new context for critical bugs
- [x] All critical bugs fixed
- [x] Code reviewed with coding standard agent in a brand new context
- [x] Coding standard violations fixed

### Task 2 — Block writes into an archived Organization

**Do.** Three pieces.

First, rename `ICallerOrganizationService.GetOrganizationIdAsync()` to `GetCallerOrganizationAsync()` and return the
whole `Organization` instead of just its Id, because the filter needs its state. That changes the method's shape, so
all 32 call sites change with it — one per action, across the eight controllers that resolve the tenant today.
Mechanical churn from the rename. No new logic in the controllers.

Second, cache the resolved Organization in `HttpContext.Items`. The filter resolves the tenant, then the action
resolves it again to scope its queries. Each resolve is two storage round trips. The cache makes the second resolve
free.

Third, add the filter and register it globally in `Program.cs`. Any request that is not a `GET`, against an
Organization that is not `Active`, returns `403`. `GET` always passes. Treat `Deleting` the same as `Archived` — it
should be unreachable for an Organization, and failing closed is the right direction.

Two decisions the implementer should not have to guess:

- Return `StatusCode(403, new ErrorResponse(...))`, not `Forbid()`. `Forbid()` returns an empty body and tangles with
  the auth scheme, so the message never reaches the caller.
- The message must name the cause plainly — something like "This organization is archived. Ask an owner to unarchive
  it." A vague message reads like a broken permission check and generates support tickets.

`AuthController` is untouched. Sign-in and token refresh never touch Organization-owned entities.

**Files.**

- `src/A2.Server/Services/ICallerOrganizationService.cs`
- `src/A2.Server/Services/CallerOrganizationService.cs`
- `src/A2.Server/Common/RequireActiveOrganizationFilter.cs` (new)
- `src/A2.Server/Program.cs`
- All eight controllers in `src/A2.Server/Controllers/`: Activities, Applications, Environments, EvidenceDefinitions,
  Preconditions, Runs, RunStatusUpdates, Scenarios.

**Tests.** In `tests/A2.Server.Tests`: against an archived Organization a `POST` returns `403` and a `GET` returns
`200`; against an active Organization nothing changes; the `403` body carries a message naming the archive; a request
that resolves the tenant twice reads the Organization row only once.

**Verify.** `grep -rn "GetOrganizationIdAsync" src/ tests/` comes back empty.

- [x] Linting pass.
- [x] Build pass.
- [x] Tests pass
- [x] Output code reviewed with a seperate agent in a fresh new context for critical bugs
- [x] All critical bugs fixed
- [x] Code reviewed with coding standard agent in a brand new context
- [x] Coding standard violations fixed

### Task 3 — Archive and unarchive endpoints

**Do.** Add an `OrganizationsController` with four endpoints:

- `POST /organizations/{id}/archive` — Owner only. Idempotent: archiving an already-archived Organization succeeds.
  Returns `204`.
- `POST /organizations/{id}/unarchive` — Owner only. Idempotent. Returns `204`.
- `GET /organizations` — the caller's Organizations, `Active` only.
- `GET /organizations/archived` — the sibling list required by the entity-lifecycle skill's Rule 3.

Add `TrySetLifecycleStateAsync` to `IOrganizationRepository`. One conditional `UpdateItem`. No transaction, no
deletion job row, no background worker — there is nothing to clean up.

Reject archiving a personal Organization with `400`. Sign-in only creates a personal Organization for a user with zero
memberships, so archiving one would lock that user into a frozen account with no way out.

Mark `unarchive` with a new `[AllowArchivedOrganization]` attribute that the Task 2 filter reads. Use the attribute,
not a path check inside the filter — Task 4 reads the same attribute, and a shared marker is what keeps the docs and
the behaviour from drifting apart. Without this, an archived Organization can never be revived.

The Owner check reads `OrganizationUser.Role`. Nothing in the codebase reads that field today; this is its first use.

**Files.**

- `src/A2.Server/Controllers/OrganizationsController.cs` (new)
- `src/A2.Server/Contracts/` — request and response DTOs for the four endpoints
- `src/A2.Server/Common/AllowArchivedOrganizationAttribute.cs` (new)
- `src/A2.Server/Common/RequireActiveOrganizationFilter.cs` — read the attribute
- `src/A2.Server/Repositories/IOrganizationRepository.cs`
- `src/A2.Server/Repositories/DynamoDbOrganizationRepository.cs`

**Tests.** In `tests/A2.Server.Tests`: an Owner can archive and unarchive; a Member gets `403`; archiving twice still
returns `204`; archiving a personal Organization returns `400`; an archived Organization disappears from
`GET /organizations` and appears in `GET /organizations/archived`; unarchive still works while the Organization is
archived, which proves the allowlist attribute is wired up.

- [x] Linting pass.
- [x] Build pass.
- [x] Tests pass
- [x] Output code reviewed with a seperate agent in a fresh new context for critical bugs
- [x] All critical bugs fixed
- [x] Code reviewed with coding standard agent in a brand new context
- [x] Coding standard violations fixed

### Task 4 — Document the `403` automatically

**Do.** Add an OpenAPI operation transformer and register it in `Program.cs` beside
`RequestValidationOperationTransformer`. That existing transformer solves the same problem for validation `400`s —
copy its shape.

The transformer's rule mirrors the filter's exactly: if the operation is not a `GET`, the endpoint requires auth, and
it carries no `[AllowArchivedOrganization]`, add a `403` describing the archived Organization. Nothing is written per
endpoint, so an endpoint added next year is documented the day it is written.

If the operation already documents a `403`, append to the existing description. Never overwrite it. Some endpoints
return `403` for their own reasons — the Owner-only checks in Task 3 do — and losing that text would make the docs
wrong in the one place someone bothered to write them. `RequestValidationOperationTransformer` already appends for
`400`s; follow it.

**Files.**

- `src/A2.Server/Common/ArchivedOrganizationOperationTransformer.cs` (new)
- `src/A2.Server/Program.cs`

**Tests.** In `tests/A2.Server.Tests`: fetch the generated OpenAPI document and assert that a sample `POST` carries
the `403`, a `GET` does not, an endpoint with `[AllowArchivedOrganization]` does not, and an endpoint with its own
`403` keeps its original description with the new text appended rather than replacing it.

- [x] Linting pass.
- [x] Build pass.
- [x] Tests pass
- [x] Output code reviewed with a seperate agent in a fresh new context for critical bugs
- [x] All critical bugs fixed
- [x] Code reviewed with coding standard agent in a brand new context
- [x] Coding standard violations fixed

## Goal complete

- [x] All four tasks above are ticked.
- [x] `dotnet build A2.Server.slnx` succeeds with no warnings.
- [x] `dotnet format A2.Server.slnx --verify-no-changes` reports no changes.
- [x] `dotnet jb inspectcode A2.Server.slnx -o=inspect.sarif.json --no-build --severity=WARNING` reports no findings.
      Fix real issues; suppress members that are intentionally not used yet.
- [x] `dotnet test tests/A2.Server.UnitTests/A2.Server.UnitTests.csproj` passes.
- [x] `dotnet test tests/A2.Server.Tests/A2.Server.Tests.csproj` passes.
- [x] `/code-review` has been run across the whole change.
- [x] Every real, critical finding from that review is fixed. Findings that are wrong or out of scope are listed with
      a one-line reason, not silently dropped.
- [x] After those fixes, build, format, inspect and both test suites were run again and all pass.
- [x] `grep -rn "GetOrganizationIdAsync" src/ tests/` comes back empty. (Service method renamed; test helper remains)
- [x] `RequireActiveOrganizationFilter` and `ArchivedOrganizationOperationTransformer` are both registered in
      `Program.cs`.
- [x] No repository gained an Organization state check. The block lives only in the filter.

## Out of scope

The design covers the Organization only. Do not start these, and do not let a code review push you into them:

- Archive or physical delete for any other entity — Application, Scenario, Precondition, Environment,
  EvidenceDefinition, Activity, Run.
- The deletion job row, the purge worker, and anything to do with `LifecycleState.Deleting`.
- Changes to `autoassure-web`.

## Open questions

1. **Who regenerates the SDK, and when?** Task 3 adds four endpoints. The project's rule is to ask before running
   `../scripts/generate-sdk.sh`. This affects whether the goal is finished when the server builds, or only once the
   SDK is regenerated too.

2. **Does the web app need a frozen-tenant banner in this batch?** Once the filter lands, someone working in an
   Organization that gets archived sees every save fail with `403`. That is correct but hostile. The fix is in
   `autoassure-web`, which this goal lists as out of scope. Confirm it is tracked separately rather than forgotten.

## Execution instructions

1. Each task is executed independently by a fresh sub-agent with fresh context.
2. The main thread observes progress only. Sub-agents do the work.
3. When a task completes, update this document to tick off completion.
4. The goal is not complete until every item in this document is ticked.
5. Before executing any task, ask the clarifying questions from "Open questions".

Two of the seven boxes on each task are reviews, and each needs its own fresh sub-agent — one that hunts correctness
bugs only, and one that loads the project's coding-standards skill. The agent that wrote the code must never review
its own work. After a review finds something, fix it, then re-run build, lint and tests before ticking.

```
/goal Work through archiving_organization.md in /Users/namduong/Documents/autoassure/autoassure-server. Done means
every checkbox in that file is ticked, including the final "Goal complete" list. For each task the proof is: `dotnet
build A2.Server.slnx` succeeds, `dotnet format A2.Server.slnx --verify-no-changes` reports no changes, `dotnet jb
inspectcode A2.Server.slnx -o=inspect.sarif.json --no-build --severity=WARNING` reports no findings, `dotnet test
tests/A2.Server.UnitTests/A2.Server.UnitTests.csproj` and `dotnet test tests/A2.Server.Tests/A2.Server.Tests.csproj`
both pass, and the file is edited to tick that task's boxes. Run the tasks in order, one sub-agent each; they depend
on each other, so never run them in parallel. Never tick a box you have not proved in this session. Never edit or
delete a test to make it pass. Never change entity_life_cycle_design.md. Stop after 40 turns and report what is left.
```
