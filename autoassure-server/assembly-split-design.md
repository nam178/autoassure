# Splitting A2.Server into separate assemblies

## Problem

Right now the whole backend is one project, `A2.Server`. Every controller, service, repository, and model lives in the same assembly. That was fine when the API was the only thing running, but we now want a separate queue-worker process (for long-running test runs) that reuses the same business logic without pulling in web-only code. One project also means there is no compiler-enforced boundary between features — nothing stops auth code from reaching into run logic, or vice versa — and the whole thing has to be built and referenced as a single block.

## Goal

Split `A2.Server` into five smaller projects, each with a clear job, so the web API and the queue worker can share business logic without sharing web-only code, and each feature area has its own compiler-enforced boundary.

**Namespaces follow the project name.** Every file moves into a namespace that starts with its new project's name, followed by its folder. For example, a model in UserManagement goes from `A2.Server.Models` to `A2.Server.UserManagement.Models`. This keeps the namespace and the project in step, so nobody has to guess where a class lives.

| Project | Namespace root | Example |
|---|---|---|
| `A2.Server.Common` | `A2.Server.Common` | `A2.Server.Common` (unchanged) |
| `A2.Server.UserManagement` | `A2.Server.UserManagement` | `A2.Server.UserManagement.Models`, `.Services`, `.Repositories`, `.Contracts` |
| `A2.Server.Engine` | `A2.Server.Engine` | `A2.Server.Engine.Models`, `.Services`, `.Repositories`, `.Contracts` |
| `A2.Server.WebApi` | `A2.Server.WebApi` | `A2.Server.WebApi.Controllers` |
| `A2.Server.Workers` | `A2.Server.Workers` | `A2.Server.Workers` |

Files that sit at the top of a project (not in a folder), such as `AuthTokenOptions` or `ErrorResponse`, use the root namespace only. Common files stay in `A2.Server.Common`. The one exception is `LifecycleState`: it moves from `A2.Server.Models` into `A2.Server.Common`.

This makes the job bigger than just moving files. Every `namespace` line and every `using` line that points at a moved file must change. The compiler will list each one, so work through the errors project by project. Also, `DynamoDbMapper` needs a real split, explained in Task 3 below.

**Tests are the safety net.** The test projects must keep passing, and we don't edit them beyond what the compiler forces. That means we only touch `using` lines, project references, and the mapper class names from Task 3. We don't change test logic, rename tests, or move tests. If a test fails after the refactor, the refactor is wrong, not the test.

## Desired directory structure

```
autoassure-server/
├── A2.Server.slnx
├── src/
│   ├── A2.Server.WebApi/            (renamed from A2.Server — HTTP host only)
│   │   ├── A2.Server.WebApi.csproj  → references Engine, UserManagement
│   │   ├── Program.cs
│   │   └── Controllers/
│   │
│   ├── A2.Server.Workers/           (new — queue-consumer host)
│   │   ├── A2.Server.Workers.csproj → references Engine
│   │   └── Program.cs
│   │
│   ├── A2.Server.Engine/            (Applications, Environments, Scenarios, Runs)
│   │   ├── A2.Server.Engine.csproj  → references UserManagement, Common
│   │   ├── Models/
│   │   ├── Services/
│   │   ├── Repositories/
│   │   └── Contracts/
│   │
│   ├── A2.Server.UserManagement/    (Auth, Organizations, Users)
│   │   ├── A2.Server.UserManagement.csproj → references Common
│   │   ├── Models/
│   │   ├── Services/
│   │   ├── Repositories/
│   │   └── Contracts/
│   │
│   └── A2.Server.Common/            (shared kernel — no feature knowledge)
│       └── A2.Server.Common.csproj
│
└── tests/
    ├── A2.Server.Tests/             → references WebApi, Engine, UserManagement (integration)
    └── A2.Server.UnitTests/         → references UserManagement, Engine (unit)
```

Dependency direction (an arrow means "depends on"):

```
WebApi ──┬──► Engine ──► UserManagement ──► Common
Workers ─┘
```

Nothing points backwards. `Common` knows about nothing else. `UserManagement` knows about `Common` only — it never learns that scenarios or runs exist. `Engine` knows about `UserManagement` and `Common`.

## Full listing: every class/file, by assembly

### A2.Server.Common
No feature knowledge. Both `UserManagement` and `Engine` depend on this.

| File | Note |
|---|---|
| `IClock`, `SystemClock` | |
| `DynamoDbOptions` | table names / region config |
| `DesignTimeBuild` | detects the OpenAPI-doc-gen headless build; rename its hard-coded assembly-name check (Task 5) |
| `DynamoDbMapper` | **base helpers only** (`RequireBool`, etc.) — see Task 3 |
| `CorruptedDynamoDbRowException` | thrown by the base helpers above |
| `NotBlankAttribute` | plain validation attribute, used by DTOs in Engine |
| `NoNullItemsAttribute` | plain validation attribute for collections, used by DTOs in Engine |
| `LifecycleState` | used by both Organization (UserManagement) and Scenario/Activity (Engine) — genuinely shared |

### A2.Server.UserManagement
Auth + Organizations + Users. Depends on Common only.

**Models:** `User`, `Organization`, `OrganizationUser`, `GoogleIdentity`, `AppToken`, `IssuedTokens`, `RefreshToken`

**Services:** `AuthTokenService` / `IAuthTokenService`, `CallerOrganizationService` / `ICallerOrganizationService`, `GoogleIdTokenValidator` / `IGoogleIdTokenValidator`, `GoogleTokenExchangeService` / `IGoogleTokenExchangeService`, `GoogleUserSyncService` / `IGoogleUserSyncService`, `ConfigValidationHostedService` (validates Google/Auth secrets on startup — registered only from WebApi's `Program.cs`)

**Repositories:** `IUserRepository`, `IOrganizationRepository`, `IOrganizationUserRepository`, `IRefreshTokenRepository` and their `DynamoDb*Repository` implementations; `OrganizationDynamoDbMapper`, `UserDynamoDbMapper`, `RefreshTokenDynamoDbMapper` (renamed, see Task 3); `OrganizationUserUpdatableFields`, `UserUpdatableFields`, `RemoveMembershipOutcome`

**Contracts:** `UserResponse`, `OrganizationResponse`, `AuthTokenResponse`, `RefreshTokenRequest`, `RefreshTokenResponse`, `ExchangeGoogleCodeRequest`

**Config/errors:** `AuthTokenOptions`, `GoogleAuthOptions`, `GoogleTokenExchangeException`

### A2.Server.Engine
Applications, Environments, Scenarios, Runs. Depends on UserManagement + Common.

**Models:** `Application`, `Environment`, `EnvironmentVariable`, `Scenario`, `Precondition`, `EvidenceDefinition`, `Activity`, `Run`, `RunInfo`, `RunningRun`, `RunStatusUpdate`, `RunActivitySnapshot`, `RunEnvironmentSnapshot`, `RunEnvironmentVariableSnapshot`, `RunEvidenceDefinitionSnapshot`, `RunPreconditionSnapshot`, `RunScenarioSnapshot`, `RunSnapshotSource`, `SensitiveValueMasker`

**Services:** `RunSnapshotBuilder` / `IRunSnapshotBuilder`

**Repositories:** `IApplicationRepository`, `IEnvironmentRepository`, `IEnvironmentVariableRepository`, `IScenarioRepository`, `IPreconditionRepository`, `IEvidenceDefinitionRepository`, `IActivityRepository`, `IRunRepository` and their `DynamoDb*Repository` implementations; `ApplicationDynamoDbMapper`, `EnvironmentDynamoDbMapper`, `ScenarioDynamoDbMapper`, `PreconditionDynamoDbMapper`, `EvidenceDefinitionDynamoDbMapper`, `ActivityDynamoDbMapper`, `RunDynamoDbMapper` (renamed, see Task 3); `EnvironmentUpdatableFields`, `PreconditionUpdatableFields`, `EvidenceDefinitionUpdatableFields`, `RunUpdatableFields`, `ScenarioUpdateResult`, `ScenarioDeleteResult`, `RunCreateResult`, `RunStartResult`, `ActivitySaveResult`, `ActivityUpdatableFields`, `ActivityUpdateResult`; `Quota`

**Contracts:** everything else in `Contracts/` not listed under UserManagement or WebApi below — e.g. `ApplicationResponse`, `CreateApplicationRequest`, `EnvironmentResponse`, `CreateEnvironmentRequest`, `UpdateEnvironmentRequest`, `SetEnvironmentVariableRequest`, `EnvironmentClassification`, `ScenarioResponse`, `CreateScenarioRequest`, `UpdateScenarioRequest`, `PreconditionResponse`, `PreconditionValueSource`, `CreatePreconditionRequest`, `UpdatePreconditionRequest`, `EvidenceDefinitionResponse`, `CreateEvidenceDefinitionRequest`, `UpdateEvidenceDefinitionRequest`, `ActivityResponse`, `ActivityResult`, `ActivityResultStatus`, `CreateActivityRequest`, `UpdateActivityRequest`, `ReorderActivitiesRequest`, `RunResponse`, `RunSummaryResponse`, `RunningRunResponse`, `RunStatus`, `RunTrigger`, `CreateRunRequest`, `EndRunRequest`, `UpdateRunStatsRequest`, `AppendRunStatusUpdateRequest`, `RunStatusUpdateKind`, `RunStatusUpdateResponse`, `RunActivitySnapshotResponse`, `RunEnvironmentSnapshotResponse`, `RunEnvironmentVariableSnapshotResponse`, `RunEvidenceDefinitionSnapshotResponse`, `RunPreconditionSnapshotResponse`, `RunScenarioSnapshotResponse`, `SnapshotSourceResponse`

### A2.Server.WebApi
The HTTP host. Depends on Engine + UserManagement.

- `Program.cs`
- All of `Controllers/` (19 files) including all `ContractMapper.*` partial files
- `ErrorResponse`
- `AllowArchivedOrganizationAttribute`, `ArchivedOrganizationOperationTransformer`, `RequireActiveOrganizationFilter`, `NotBlankSchemaTransformer`, `NoNullItemsSchemaTransformer`, `RequestValidationOperationTransformer`, `ClaimsPrincipalExtensions`

### A2.Server.Workers
New project — nothing moves here yet. Just:
- `Program.cs`: a generic-host (not `WebApplication`) entry point that wires up the same DynamoDB/config setup as WebApi, then starts the queue-consumer loop.

## Task breakdown

Do these roughly in order. Run `dotnet build A2.Server.slnx` after every task — it should succeed (or fail only in the expected/explained way) before moving to the next one.

1. **Create `A2.Server.Common`.** Add a new class library project. Move `IClock.cs`, `SystemClock.cs`, `DynamoDbOptions.cs`, `DesignTimeBuild.cs`, `NotBlankAttribute.cs`, `NoNullItemsAttribute.cs`, `CorruptedDynamoDbRowException.cs` into it. Move `Models/LifecycleState.cs` into it too. Put every moved file in the `A2.Server.Common` namespace (only `LifecycleState` needs its `namespace` line changed, and the `using A2.Server.Models;` lines that relied on it must become `using A2.Server.Common;`). Do **not** move `DynamoDbMapper.cs` yet — that's Task 3.

2. **Create `A2.Server.UserManagement` and `A2.Server.Engine`.** Add both as class library projects, each referencing `A2.Server.Common`. Don't move any files into them yet — that happens in Tasks 3–4. Wire up the reference `Engine → UserManagement` now.

3. **Split `DynamoDbMapper` before moving anything else in `Repositories/`.** Today it's one C# `partial class` spread across 11 files. Partial class parts must live in the same assembly, so it cannot stay split across UserManagement and Engine as-is. For each of these files:
   - `DynamoDbMapper.Organization.cs` → rename class to `OrganizationDynamoDbMapper`, remove `partial`, move to UserManagement.
   - `DynamoDbMapper.User.cs` → `UserDynamoDbMapper`, move to UserManagement.
   - `DynamoDbMapper.RefreshToken.cs` → `RefreshTokenDynamoDbMapper`, move to UserManagement.
   - `DynamoDbMapper.Application.cs` → `ApplicationDynamoDbMapper`, move to Engine.
   - `DynamoDbMapper.Environment.cs` → `EnvironmentDynamoDbMapper`, move to Engine.
   - `DynamoDbMapper.Scenario.cs` → `ScenarioDynamoDbMapper`, move to Engine.
   - `DynamoDbMapper.Precondition.cs` → `PreconditionDynamoDbMapper`, move to Engine.
   - `DynamoDbMapper.EvidenceDefinition.cs` → `EvidenceDefinitionDynamoDbMapper`, move to Engine.
   - `DynamoDbMapper.Activity.cs` → `ActivityDynamoDbMapper`, move to Engine.
   - `DynamoDbMapper.Run.cs` → `RunDynamoDbMapper`, move to Engine.
   - `DynamoDbMapper.cs` (the base file) stays as `DynamoDbMapper`, drop `partial` (it's no longer split), move to `A2.Server.Common`.
   Then fix every call site: anywhere the code called `DynamoDbMapper.ToRun(...)`, `DynamoDbMapper.ToOrganization(...)`, etc., change the class name at the call site to match (e.g. `RunDynamoDbMapper.ToRun(...)`). Your IDE's "rename symbol" won't do this automatically since these are now different classes — use find-and-replace per method name, or let the compiler errors guide you file by file.

4. **Move the rest of `Repositories/`, `Models/`, `Services/`, `Contracts/` into UserManagement and Engine** following the full listing above. Classes keep their names. Only the file location and the `namespace` line change (for example `A2.Server.Repositories` becomes `A2.Server.Engine.Repositories`). Update every `using` that points at a moved file, including the ones in the Task 3 mappers. Fix each project's `.csproj` package references as build errors point them out (e.g. `AWSSDK.DynamoDBv2` is needed wherever a repository uses `IAmazonDynamoDB` or `AttributeValue`; `Google.Apis.Auth` is needed in UserManagement only).

5. **Rename the web project.** Rename `src/A2.Server` → `src/A2.Server.WebApi`, its `.csproj` → `A2.Server.WebApi.csproj`, and set `<AssemblyName>A2.Server.WebApi</AssemblyName>`. Move the remaining files (`Controllers/`, `Program.cs`, `ErrorResponse.cs`, the OpenAPI/filter/attribute files listed above) — most of them are already in place since this project started as the original one. Add project references to `Engine` and `UserManagement`. Update `DesignTimeBuild.cs`'s hard-coded check from `!= "A2.Server"` to `!= "A2.Server.WebApi"`. Set `<RootNamespace>A2.Server.WebApi</RootNamespace>`, and move the controllers and web files into `A2.Server.WebApi.*` namespaces. The `OpenApiDocumentsDirectory` path in the `.csproj` stays valid, because the project stays at the same folder depth.

6. **Create `A2.Server.Workers`.** New console/worker-SDK project, references `Engine` only. `Program.cs` uses `Host.CreateApplicationBuilder` (not `WebApplication`), wires up the same DynamoDB client and configuration loading as WebApi's `Program.cs` (copy the relevant bits, not all of them — no JWT bearer, no OpenAPI, no MVC filters). Add whatever queue-consuming loop we build later; for this task, an empty loop that logs "worker started" is enough to prove the wiring works.

7. **Add both projects to `A2.Server.slnx`.** Also change the old `src/A2.Server/A2.Server.csproj` entry to `src/A2.Server.WebApi/A2.Server.WebApi.csproj`.

8. **Fix the test projects.** Keep changes to the bare minimum (see "Tests are the safety net" above).
   - `A2.Server.UnitTests.csproj`: replace its single `ProjectReference` to the old `A2.Server.csproj` with references to `A2.Server.UserManagement.csproj` and `A2.Server.Engine.csproj`.
   - `A2.Server.Tests.csproj`: replace its `ProjectReference` with references to `A2.Server.WebApi.csproj`, `A2.Server.Engine.csproj` and `A2.Server.UserManagement.csproj`. It needs all three because it holds the controller tests (which use `WebApplicationFactory` against the web host) and the repository tests for both feature areas.
   - Update `using` lines to the new namespaces. Test files keep their own namespaces (`A2.Server.Tests`, `A2.Server.UnitTests`).
   - Update the mapper class names at call sites (per Task 3), for example `DynamoDbMapper.ToRun(...)` becomes `RunDynamoDbMapper.ToRun(...)`. Do not rename or move the test files.
   - `QuotaTests.cs` tests `Quota`, which moves to Engine. The Engine reference already covers it.
   - Make no other changes. No test logic, assertions or test names change.

9. **Update everything outside the code that points at the server.** We checked the repo. Only these places refer to the old project:
   - `scripts/generate-sdk.sh`: it runs `dotnet build "$SERVER_DIR"`, which builds the solution. Point it at `src/A2.Server.WebApi/A2.Server.WebApi.csproj` instead, so it builds only the web project. It also takes the first `*.json` in `autoassure-server-sdk/.spec/`. The spec file name comes from the assembly name, so delete the old `A2.Server.json` there first, or the script may pick the stale file.
   - `autoassure-server/CLAUDE.md`: update the "Structure" section to describe the five projects.
   - `autoassure-server/.claude/skills/*`: check for paths like `src/A2.Server/...`. The `testing-guideline` skill only names the two test projects, which don't change.
   - `autoassure-server-sdk/openapi.json` and `src/Api.ts`: the API title changes from `A2.Server | v1` to match the new assembly name. Regenerate both by running `scripts/generate-sdk.sh` once the split builds.
   - `autoassure-web`, `autoassure-infra` (Terraform), CI and Docker files: no references found today. Nothing to change.

10. **Final check.** Run the full verification sequence from `CLAUDE.md`: `dotnet build A2.Server.slnx`, `dotnet format A2.Server.slnx`, `dotnet jb inspectcode A2.Server.slnx -o=inspect.sarif.json --no-build --severity=WARNING`, then both test projects. Fix everything the build/format/inspect steps report before considering this done. Both test projects must pass with only the minimal edits from Task 8.
