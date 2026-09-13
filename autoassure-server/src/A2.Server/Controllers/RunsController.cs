using System.Diagnostics;
using A2.Server.Common;
using A2.Server.Contracts;
using A2.Server.Models;
using A2.Server.Repositories;
using A2.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ContractRunStatus = A2.Server.Contracts.RunStatus;
using Environment = A2.Server.Models.Environment;
using ModelRunStatus = A2.Server.Models.RunStatus;
using ModelRunTrigger = A2.Server.Models.RunTrigger;
using Run = A2.Server.Models.Run;

namespace A2.Server.Controllers;

/// <summary>Manual Runs, nested under their Application, and the shared state-machine operations
/// (Start/End/Heartbeat/Update Stats) every Run goes through regardless of how it was created. The
/// Authoring create path lives on <see cref="AuthoringRunsController"/> instead, since it is a flat route
/// over a Scenario rather than a nested one over an Application.</summary>
[ApiController]
[Authorize]
public class RunsController(
    IApplicationRepository applicationRepository,
    IEnvironmentRepository environmentRepository,
    IScenarioRepository scenarioRepository,
    IRunRepository runRepository,
    IRunSnapshotBuilder runSnapshotBuilder,
    ICallerOrganizationService callerOrganizationService,
    IClock clock
) : ControllerBase
{
    /// <response code="400">EnvironmentId does not reference an Environment belonging to this
    /// Application, ScenarioIds contains a duplicate, or ScenarioIds contains an id that does not
    /// reference a Scenario belonging to this Application.</response>
    /// <response code="404">No Application with the given appId exists in the caller's Organization, or
    /// it no longer exists (deleted after this request started).</response>
    [HttpPost("applications/{appId:guid}/runs", Name = "CreateRun")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RunResponse>> Create(Guid appId, CreateRunRequest request)
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();

        // The Application is the URL resource -- its non-existence must win as a 404 over a 400 for a
        // semantically invalid body (an EnvironmentId/ScenarioIds that don't exist), so it is checked
        // before validating those below. A structurally invalid body (missing field, wrong type) fails
        // ASP.NET's automatic model validation before this action ever runs, and gets 400 like any other
        // endpoint in this codebase.
        if (await applicationRepository.GetByIdAsync(organizationId, appId) is null)
        {
            return NotFound();
        }

        if (request.ScenarioIds.Distinct().Count() != request.ScenarioIds.Count)
        {
            return BadRequest(new ErrorResponse("ScenarioIds must not contain duplicates."));
        }

        var environment = await environmentRepository.GetByIdAsync(
            organizationId,
            request.EnvironmentId
        );
        if (environment is null || environment.ApplicationId != appId)
        {
            return BadRequest(
                new ErrorResponse(
                    "EnvironmentId does not reference an Environment belonging to this Application."
                )
            );
        }

        var scenarios = new List<Scenario>(request.ScenarioIds.Count);
        foreach (var scenarioId in request.ScenarioIds)
        {
            var scenario = await scenarioRepository.GetByIdAsync(organizationId, scenarioId);
            if (scenario is null || scenario.ApplicationId != appId)
            {
                return BadRequest(
                    new ErrorResponse(
                        "ScenarioIds contains an id that does not reference a Scenario belonging to "
                            + "this Application."
                    )
                );
            }

            scenarios.Add(scenario);
        }

        return await CreateRunAsync(
            organizationId,
            appId,
            ModelRunTrigger.Manual,
            environment,
            scenarios,
            runSnapshotBuilder,
            runRepository,
            clock,
            User.GetUserId()
        );
    }

    /// <summary>Never returns Authoring Runs -- those are scratch runs against a Scenario under
    /// construction, not runs of the Application's saved Scenarios.</summary>
    [HttpGet("applications/{appId:guid}/runs", Name = "ListRuns")]
    public async Task<ActionResult<IReadOnlyList<RunSummaryResponse>>> List(Guid appId)
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var runs = await runRepository.ListByApplicationAsync(
            organizationId,
            appId,
            [ModelRunTrigger.Manual, ModelRunTrigger.Scheduled]
        );
        return Ok(runs.Select(r => r.ToResponse()).ToList());
    }

    /// <summary>Lists the Runs currently Running for this Application, strongly consistent -- a Run that
    /// just started is never briefly missing from this result, unlike <see cref="List"/>.</summary>
    [HttpGet("applications/{appId:guid}/runs/running", Name = "ListRunningRuns")]
    public async Task<ActionResult<IReadOnlyList<RunningRunResponse>>> ListRunning(Guid appId)
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var runs = await runRepository.ListRunningByApplicationAsync(organizationId, appId);
        return Ok(runs.Select(r => r.ToResponse()).ToList());
    }

    /// <response code="404">No Run with the given runId exists in this Application, in the caller's
    /// Organization.</response>
    [HttpGet("applications/{appId:guid}/runs/{runId:guid}", Name = "GetRunById")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RunResponse>> GetById(Guid appId, Guid runId)
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var run = await runRepository.GetByIdAsync(organizationId, appId, runId);
        return run is null ? NotFound() : Ok(run.ToResponse());
    }

    /// <summary>Claims a Pending Run for execution and returns it, unmasked, to the winning caller only
    /// -- the one time in this API's life a sensitive Environment variable's real value is ever returned.
    /// Every other response (Create Run, Get Run) always masks sensitive values regardless of what
    /// storage currently holds; see <see cref="ContractMapper.ToResponse(Run, bool)"/>.</summary>
    /// <response code="404">No Run with the given runId exists in this Application, in the caller's
    /// Organization.</response>
    /// <response code="409">The Run's Status is not Pending.</response>
    [HttpPost("applications/{appId:guid}/runs/{runId:guid}/start", Name = "StartRun")]
    [ProducesResponseType(typeof(RunResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RunResponse>> Start(Guid appId, Guid runId)
    {
        // TODO:  UpdateHeartbeat, UpdateStats, and End all call the full GetByIdAsync purely as
        //                               an existence check before a narrow, already-conditioned write, on the hottest
        //                               write path.
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();

        // A Run's own existence is checked with a Get first, since TryMarkAsStartedAsync's condition alone
        // cannot tell "does not exist" apart from "exists but is not Pending" -- both fail the same way.
        // This read happens before TryMarkAsStartedAsync's write below, so run.Environment still holds the
        // real (unmasked) values -- TryMarkAsStartedAsync has not yet overwritten storage with the masked
        // snapshot computed from it.
        var run = await runRepository.GetByIdAsync(organizationId, appId, runId);
        if (run is null)
        {
            return NotFound();
        }

        var maskedEnvironment = run.Environment.Masked();
        var started = await runRepository.TryMarkAsStartedAsync(
            organizationId,
            appId,
            runId,
            clock.UtcNow,
            maskedEnvironment
        );
        if (started is null)
        {
            return Conflict(new ErrorResponse("The Run's Status is not Pending."));
        }

        // When the claim wins, Then this applies exactly the values TryMarkAsStartedAsync reports it wrote, so
        // the response reflects what was actually persisted instead of a second, independent derivation
        // that could drift from the repository's own. Environment is deliberately left as the original
        // unmasked snapshot, not the masked one just written to storage, since this response is the
        // winning caller's one chance to get the real values back.
        var startedRun = run with
        {
            Status = ModelRunStatus.Running,
            StartedAt = started.StartedAt,
            LastHeartbeatAt = started.LastHeartbeatAt,
        };
        return Ok(startedRun.ToResponse(maskSensitiveValues: false));
    }

    /// <response code="400">TerminalStatus is Pending or Running, or StatusReason is set while
    /// TerminalStatus is not Abandoned.</response>
    /// <response code="404">No Run with the given runId exists in this Application, in the caller's
    /// Organization.</response>
    /// <response code="409">The Run's Status is not Running.</response>
    [HttpPost("applications/{appId:guid}/runs/{runId:guid}/end", Name = "EndRun")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult> End(Guid appId, Guid runId, EndRunRequest request)
    {
        if (
            request.TerminalStatus != ContractRunStatus.Completed
            && request.TerminalStatus != ContractRunStatus.Cancelled
            && request.TerminalStatus != ContractRunStatus.Abandoned
        )
        {
            return BadRequest(
                new ErrorResponse("TerminalStatus must be Completed, Cancelled or Abandoned.")
            );
        }

        if (
            request.StatusReason is not null
            && request.TerminalStatus != ContractRunStatus.Abandoned
        )
        {
            return BadRequest(
                new ErrorResponse("StatusReason can only be set when TerminalStatus is Abandoned.")
            );
        }

        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        if (await runRepository.GetByIdAsync(organizationId, appId, runId) is null)
        {
            return NotFound();
        }

        var ended = await runRepository.TryMarkAsEndedAsync(
            organizationId,
            appId,
            runId,
            request.TerminalStatus.ToModel(),
            request.StatusReason?.ToModel(),
            clock.UtcNow
        );
        return ended
            ? NoContent()
            : Conflict(new ErrorResponse("The Run's Status is not Running."));
    }

    /// <response code="404">No Run with the given runId exists in this Application, in the caller's
    /// Organization.</response>
    /// <response code="409">The Run's Status is not Running.</response>
    [HttpPost("applications/{appId:guid}/runs/{runId:guid}/heartbeat", Name = "UpdateRunHeartbeat")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Heartbeat(Guid appId, Guid runId)
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        if (await runRepository.GetByIdAsync(organizationId, appId, runId) is null)
        {
            return NotFound();
        }

        var beat = await runRepository.TryUpdateAsync(
            organizationId,
            appId,
            runId,
            new RunUpdatableFields { HeartbeatAt = clock.UtcNow }
        );
        return beat ? NoContent() : Conflict(new ErrorResponse("The Run's Status is not Running."));
    }

    /// <response code="404">No Run with the given runId exists in this Application, in the caller's
    /// Organization.</response>
    /// <response code="409">The Run's Status is not Running.</response>
    [HttpPost("applications/{appId:guid}/runs/{runId:guid}/stats", Name = "UpdateRunStats")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult> UpdateStats(
        Guid appId,
        Guid runId,
        UpdateRunStatsRequest request
    )
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        if (await runRepository.GetByIdAsync(organizationId, appId, runId) is null)
        {
            return NotFound();
        }

        var updated = await runRepository.TryUpdateAsync(
            organizationId,
            appId,
            runId,
            new RunUpdatableFields
            {
                TotalActivityCount = request.TotalActivityCount,
                PassedActivityCount = request.PassedActivityCount,
                FailedActivityCount = request.FailedActivityCount,
                SkippedActivityCount = request.SkippedActivityCount,
            }
        );
        return updated
            ? NoContent()
            : Conflict(new ErrorResponse("The Run's Status is not Running."));
    }

    /// <summary>Shared by Create Run (Manual) and Create Run (Authoring, on
    /// <see cref="AuthoringRunsController"/>): builds every snapshot, creates the Run, and maps the
    /// result to the same response shape both create paths return.</summary>
    internal static async Task<ActionResult<RunResponse>> CreateRunAsync(
        Guid organizationId,
        Guid applicationId,
        ModelRunTrigger trigger,
        Environment environment,
        IReadOnlyList<Scenario> scenarios,
        IRunSnapshotBuilder runSnapshotBuilder,
        IRunRepository runRepository,
        IClock clock,
        Guid? triggeredByUserId
    )
    {
        var environmentSnapshot = await runSnapshotBuilder.BuildEnvironmentSnapshotAsync(
            organizationId,
            environment
        );
        var scenarioSnapshots = await runSnapshotBuilder.BuildScenarioSnapshotsAsync(
            organizationId,
            applicationId,
            scenarios
        );

        // TotalActivityCount is counted from the snapshot just built, never read off the live
        // Scenario's own denormalized ActivityCount -- that field can drift (see
        // fix_run_design.md section 5 and the `// BUG:` in DynamoDbActivityRepository).
        var run = new Run
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            Trigger = trigger,
            Environment = environmentSnapshot,
            Scenarios = scenarioSnapshots,
            Status = ModelRunStatus.Pending,
            TotalActivityCount = scenarioSnapshots.Sum(s => s.Activities.Count),
            TriggeredByUserId = triggeredByUserId,
            CreatedAt = clock.UtcNow,
        };

        var result = await runRepository.TryCreateAsync(run);
        return result switch
        {
            RunCreateResult.Success => new OkObjectResult(run.ToResponse()),
            RunCreateResult.ApplicationNotFound => new NotFoundResult(),
            // Run.Id is a freshly generated UUIDv7, so this can only mean an id collision -- not
            // something a retry or a different request body can fix, but still a real enum value this
            // switch must handle rather than assume away.
            RunCreateResult.AlreadyExists => new ConflictObjectResult(
                new ErrorResponse("A Run with this Id already exists.")
            ),
            _ => throw new UnreachableException($"Unhandled {nameof(RunCreateResult)}: {result}"),
        };
    }
}
