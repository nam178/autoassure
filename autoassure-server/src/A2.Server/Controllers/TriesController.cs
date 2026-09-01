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
public class TriesController(
    IScenarioRepository scenarioRepository,
    IEnvironmentRepository environmentRepository,
    IRunRepository runRepository,
    ICallerOrganizationService callerOrganizationService
) : ControllerBase
{
    /// <response code="400">EnvironmentId does not reference an Environment belonging to the
    /// Scenario's Application, or the Application/Environment no longer exists (deleted after this
    /// request started).</response>
    /// <response code="404">No Scenario with the given id exists in the caller's Organization.</response>
    [HttpPost("scenarios/{id:guid}/try", Name = "CreateTry")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TryScenarioResponse>> Create(Guid id, CreateTryRequest request)
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var scenario = await scenarioRepository.GetByIdAsync(organizationId, id);
        if (scenario is null)
        {
            return NotFound();
        }

        // A Try must target a real Environment belonging to the Scenario's Application -- it can
        // never execute without one.
        var environment = await environmentRepository.GetByIdAsync(
            organizationId,
            request.EnvironmentId
        );
        if (environment is null || environment.ApplicationId != scenario.ApplicationId)
        {
            return BadRequest(
                new ErrorResponse(
                    "EnvironmentId does not reference an Environment belonging to the Scenario's Application."
                )
            );
        }

        var userId = User.GetUserId();
        var run = new Run
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            Kind = RunKind.Try,
            ApplicationId = scenario.ApplicationId,
            ScenarioIds = [id],
            EnvironmentId = request.EnvironmentId,
            Status = ModelRunStatus.Pending,
            TriggeredByUserId = userId,
            UpdatedByUserId = userId,
        };

        // The Scenario/Environment existed above but may have been deleted since -- not the
        // client-facing "resource" they asked for, so this is a 400, not a 404.
        if (!await runRepository.TrySaveAsync(run))
        {
            return BadRequest(
                new ErrorResponse(
                    "Application or Environment could not be found or has been deleted."
                )
            );
        }
        return Ok(run.ToTryResponse());
    }

    /// <response code="404">No Try (Run) with the given id exists in the caller's Organization.</response>
    [HttpGet("tries/{id:guid}", Name = "GetTryById")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TryScenarioResponse>> GetById(Guid id)
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var run = await runRepository.GetAsync(organizationId, id, RunKind.Try);
        return run is null ? NotFound() : Ok(run.ToTryResponse());
    }
}
