using System.Diagnostics;
using A2.Server.Common;
using A2.Server.Contracts;
using A2.Server.Repositories;
using A2.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ContractRunStatus = A2.Server.Contracts.RunStatus;
using ModelRunStatus = A2.Server.Models.RunStatus;
using ModelRunTrigger = A2.Server.Models.RunTrigger;
using Run = A2.Server.Models.Run;

namespace A2.Server.Controllers;

/// <summary>
///     Manual Runs, nested under their Application, and the shared state-machine
///     operations
///     (Start/End/Heartbeat/Update Stats) every Run goes through regardless of how
///     it was created.
/// </summary>
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
    /// <response code="400">
    ///     EnvironmentId does not reference an Environment belonging to this
    ///     Application, ScenarioIds contains a duplicate, or ScenarioIds contains an
    ///     id that does not
    ///     reference a Scenario belonging to this Application.
    /// </response>
    /// <response code="404">
    ///     No Application with the given applicationId exists in the caller's
    ///     Organization, or
    ///     it no longer exists (deleted after this request started).
    /// </response>
    [HttpPost("applications/{applicationId:guid}/runs", Name = "CreateRun")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ErrorResponse),
        StatusCodes.Status400BadRequest
    )]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RunResponse>> Create(
        Guid applicationId,
        CreateRunRequest request
    )
    {
        var callerOrganization =
            await callerOrganizationService.GetCallerOrganizationAsync();
        var organizationId = callerOrganization.Id;

        if (
            await applicationRepository.GetByIdAsync(
                organizationId,
                applicationId
            )
            is null
        )
            return NotFound();

        if (request.ScenarioIds.Distinct().Count() != request.ScenarioIds.Count)
            return BadRequest(
                new ErrorResponse("ScenarioIds must not contain duplicates.")
            );

        var environment = await environmentRepository.GetByIdAsync(
            organizationId,
            request.EnvironmentId
        );
        if (environment is null || environment.ApplicationId != applicationId)
            return BadRequest(
                new ErrorResponse(
                    "EnvironmentId does not reference an Environment belonging to this Application."
                )
            );

        var foundScenarios = await scenarioRepository.GetByIdsAsync(
            organizationId,
            applicationId,
            request.ScenarioIds
        );
        var foundScenariosById = foundScenarios.ToDictionary(scenario =>
            scenario.Id
        );
        if (
            request.ScenarioIds.Any(scenarioId =>
                !foundScenariosById.ContainsKey(scenarioId)
            )
        )
            return BadRequest(
                new ErrorResponse(
                    "ScenarioIds contains an id that does not reference a Scenario belonging to "
                        + "this Application."
                )
            );

        var scenarios = request
            .ScenarioIds.Select(scenarioId => foundScenariosById[scenarioId])
            .ToList();

        var environmentSnapshot =
            await runSnapshotBuilder.BuildEnvironmentSnapshotAsync(
                organizationId,
                environment
            );
        var scenarioSnapshots =
            await runSnapshotBuilder.BuildScenarioSnapshotsAsync(
                organizationId,
                applicationId,
                scenarios
            );

        var run = new Run
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            Trigger = ModelRunTrigger.Manual,
            Environment = environmentSnapshot,
            Scenarios = scenarioSnapshots,
            Status = ModelRunStatus.Pending,
            TotalActivityCount = scenarioSnapshots.Sum(s => s.Activities.Count),
            TriggeredByUserId = User.GetUserId(),
            CreatedAt = clock.UtcNow,
        };

        var result = await runRepository.TryCreateAsync(run);
        return result switch
        {
            RunCreateResult.Success => Ok(run.ToResponse()),
            RunCreateResult.ApplicationNotFound => NotFound(),
            RunCreateResult.AlreadyExists => Conflict(
                new ErrorResponse("A Run with this Id already exists.")
            ),
            _ => throw new UnreachableException(
                $"Unhandled {nameof(RunCreateResult)}: {result}"
            ),
        };
    }

    [HttpGet("applications/{applicationId:guid}/runs", Name = "ListRuns")]
    public async Task<ActionResult<IReadOnlyList<RunSummaryResponse>>> List(
        Guid applicationId
    )
    {
        var callerOrganization =
            await callerOrganizationService.GetCallerOrganizationAsync();
        var organizationId = callerOrganization.Id;
        var runs = await runRepository.ListByApplicationAsync(
            organizationId,
            applicationId,
            [ModelRunTrigger.Manual, ModelRunTrigger.Scheduled]
        );
        return Ok(runs.Select(r => r.ToResponse()).ToList());
    }

    /// <summary>
    ///     Lists the Runs currently Running for this Application, strongly consistent
    ///     -- a Run that
    ///     just started is never briefly missing from this result, unlike
    ///     <see cref="List" />.
    /// </summary>
    [HttpGet(
        "applications/{applicationId:guid}/runs/running",
        Name = "ListRunningRuns"
    )]
    public async Task<
        ActionResult<IReadOnlyList<RunningRunResponse>>
    > ListRunning(Guid applicationId)
    {
        var callerOrganization =
            await callerOrganizationService.GetCallerOrganizationAsync();
        var organizationId = callerOrganization.Id;
        var runs = await runRepository.ListRunningByApplicationAsync(
            organizationId,
            applicationId
        );
        return Ok(runs.Select(r => r.ToResponse()).ToList());
    }

    /// <response code="404">
    ///     No Run with the given runId exists in this Application, in the caller's
    ///     Organization.
    /// </response>
    [HttpGet(
        "applications/{applicationId:guid}/runs/{runId:guid}",
        Name = "GetRunById"
    )]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RunResponse>> GetById(
        Guid applicationId,
        Guid runId
    )
    {
        var callerOrganization =
            await callerOrganizationService.GetCallerOrganizationAsync();
        var organizationId = callerOrganization.Id;
        var run = await runRepository.GetByIdAsync(
            organizationId,
            applicationId,
            runId
        );
        return run is null ? NotFound() : Ok(run.ToResponse());
    }

    /// <summary>
    ///     Starts a Run and returns it with Environment variable values unmasked --
    ///     the last
    ///     chance to see them unmasked. After the Run starts, every response masks
    ///     them, for
    ///     security.
    /// </summary>
    /// <response code="404">
    ///     No Run with the given runId exists in this Application, in the caller's
    ///     Organization.
    /// </response>
    /// <response code="409">The Run's Status is not Pending.</response>
    [HttpPost(
        "applications/{applicationId:guid}/runs/{runId:guid}/start",
        Name = "StartRun"
    )]
    [ProducesResponseType(typeof(RunResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RunResponse>> Start(
        Guid applicationId,
        Guid runId
    )
    {
        var callerOrganization =
            await callerOrganizationService.GetCallerOrganizationAsync();
        var organizationId = callerOrganization.Id;

        var run = await runRepository.GetByIdAsync(
            organizationId,
            applicationId,
            runId
        );
        if (run is null)
            return NotFound();

        var maskedEnvironment = run.Environment.Masked();
        var started = await runRepository.TryMarkAsStartedAsync(
            organizationId,
            applicationId,
            runId,
            clock.UtcNow,
            maskedEnvironment
        );
        if (started is null)
            return Conflict(
                new ErrorResponse("The Run's Status is not Pending.")
            );

        var startedRun = run with
        {
            Status = ModelRunStatus.Running,
            StartedAt = started.StartedAt,
            LastHeartbeatAt = started.LastHeartbeatAt,
        };
        return Ok(startedRun.ToResponse(false));
    }

    /// <response code="400">TerminalStatus is Pending or Running.</response>
    /// <response code="404">
    ///     No Run with the given runId exists in this Application, in the caller's
    ///     Organization.
    /// </response>
    /// <response code="409">The Run's Status is not Running.</response>
    [HttpPost(
        "applications/{applicationId:guid}/runs/{runId:guid}/end",
        Name = "EndRun"
    )]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(
        typeof(ErrorResponse),
        StatusCodes.Status400BadRequest
    )]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult> End(
        Guid applicationId,
        Guid runId,
        EndRunRequest request
    )
    {
        if (
            request.TerminalStatus != ContractRunStatus.Completed
            && request.TerminalStatus != ContractRunStatus.Cancelled
            && request.TerminalStatus != ContractRunStatus.Abandoned
        )
            return BadRequest(
                new ErrorResponse(
                    "TerminalStatus must be Completed, Cancelled or Abandoned."
                )
            );

        var callerOrganization =
            await callerOrganizationService.GetCallerOrganizationAsync();
        var organizationId = callerOrganization.Id;
        if (
            await runRepository.GetByIdAsync(
                organizationId,
                applicationId,
                runId
            )
            is null
        )
            return NotFound();

        var ended = await runRepository.TryMarkAsEndedAsync(
            organizationId,
            applicationId,
            runId,
            request.TerminalStatus.ToModel(),
            clock.UtcNow
        );
        return ended
            ? NoContent()
            : Conflict(new ErrorResponse("The Run's Status is not Running."));
    }

    /// <response code="404">
    ///     No Run with the given runId exists in this Application, in the caller's
    ///     Organization.
    /// </response>
    /// <response code="409">The Run's Status is not Running.</response>
    [HttpPost(
        "applications/{applicationId:guid}/runs/{runId:guid}/heartbeat",
        Name = "UpdateRunHeartbeat"
    )]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Heartbeat(Guid applicationId, Guid runId)
    {
        var callerOrganization =
            await callerOrganizationService.GetCallerOrganizationAsync();
        var organizationId = callerOrganization.Id;
        if (
            await runRepository.GetByIdAsync(
                organizationId,
                applicationId,
                runId
            )
            is null
        )
            return NotFound();

        var beat = await runRepository.TryUpdateAsync(
            organizationId,
            applicationId,
            runId,
            new RunUpdatableFields { HeartbeatAt = clock.UtcNow }
        );
        return beat
            ? NoContent()
            : Conflict(new ErrorResponse("The Run's Status is not Running."));
    }

    /// <response code="404">
    ///     No Run with the given runId exists in this Application, in the caller's
    ///     Organization.
    /// </response>
    /// <response code="409">The Run's Status is not Running.</response>
    [HttpPost(
        "applications/{applicationId:guid}/runs/{runId:guid}/stats",
        Name = "UpdateRunStats"
    )]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult> UpdateStats(
        Guid applicationId,
        Guid runId,
        UpdateRunStatsRequest request
    )
    {
        var callerOrganization =
            await callerOrganizationService.GetCallerOrganizationAsync();
        var organizationId = callerOrganization.Id;
        if (
            await runRepository.GetByIdAsync(
                organizationId,
                applicationId,
                runId
            )
            is null
        )
            return NotFound();

        var updated = await runRepository.TryUpdateAsync(
            organizationId,
            applicationId,
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
}
