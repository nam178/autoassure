using A2.Server.Common;
using A2.Server.Contracts;
using A2.Server.Repositories;
using A2.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ModelRunStatus = A2.Server.Models.RunStatus;
using Run = A2.Server.Models.Run;
using RunKind = A2.Server.Models.RunKind;

namespace A2.Server.Controllers;

[ApiController]
[Authorize]
public class RunsController(
    IApplicationRepository applicationRepository,
    IEnvironmentRepository environmentRepository,
    IRunRepository runRepository,
    ICallerOrganizationService callerOrganizationService
) : ControllerBase
{
    /// <response code="400">EnvironmentId does not reference an Environment belonging to this Application.</response>
    /// <response code="404">No Application with the given appId exists in the caller's Organization.</response>
    [HttpPost("applications/{appId:guid}/runs")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RunResponse>> Create(Guid appId, CreateRunRequest request)
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        if (await applicationRepository.GetByIdAsync(organizationId, appId) is null)
        {
            return NotFound();
        }

        // A Run must target a real Environment belonging to this Application -- it can never execute without one.
        var environment = await environmentRepository.GetByIdAsync(
            organizationId,
            request.EnvironmentId
        );
        if (environment is null || environment.ApplicationId != appId)
        {
            return BadRequest();
        }

        var userId = User.GetUserId();
        var run = new Run
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            Kind = RunKind.Run,
            ApplicationId = appId,
            ScenarioIds = request.ScenarioIds,
            EnvironmentId = request.EnvironmentId,
            Status = ModelRunStatus.Pending,
            TriggeredByUserId = userId,
            UpdatedByUserId = userId,
        };

        if (!await runRepository.TrySaveAsync(run))
        {
            return NotFound();
        }
        return Ok(run.ToResponse());
    }

    [HttpGet("applications/{appId:guid}/runs")]
    public async Task<ActionResult<IReadOnlyList<RunResponse>>> List(Guid appId)
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var runs = await runRepository.ListRunsByApplicationAsync(organizationId, appId);
        return Ok(runs.Select(r => r.ToResponse()).ToList());
    }

    /// <response code="404">No Run with the given id exists in the caller's Organization.</response>
    [HttpGet("runs/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RunResponse>> GetById(Guid id)
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var run = await runRepository.GetAsync(organizationId, id, RunKind.Run);
        return run is null ? NotFound() : Ok(run.ToResponse());
    }
}
