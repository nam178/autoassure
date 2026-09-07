using A2.Server.Common;
using A2.Server.Contracts;
using A2.Server.Repositories;
using A2.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ModelRunTrigger = A2.Server.Models.RunTrigger;

namespace A2.Server.Controllers;

/// <summary>Create Run (Authoring): the Try button's flat route over a single Scenario. Kept separate
/// from <see cref="RunsController"/> because this route is not nested under an Application -- the
/// Scenario is the URL resource -- while every other Run endpoint is. Shares the actual snapshot-and-
/// create logic with Manual create via <see cref="RunsController.CreateRunAsync"/>, so both paths stay
/// exactly in step.</summary>
[ApiController]
[Authorize]
public class AuthoringRunsController(
    IScenarioRepository scenarioRepository,
    IEnvironmentRepository environmentRepository,
    IRunSnapshotBuilder runSnapshotBuilder,
    IRunRepository runRepository,
    ICallerOrganizationService callerOrganizationService,
    IClock clock
) : ControllerBase
{
    /// <response code="400">EnvironmentId does not reference an Environment belonging to the Scenario's
    /// Application.</response>
    /// <response code="404">No Scenario with the given id exists in the caller's Organization.</response>
    [HttpPost("scenarios/{id:guid}/runs", Name = "CreateAuthoringRun")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RunResponse>> Create(Guid id, CreateAuthoringRunRequest request)
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();

        // The Scenario is this route's URL resource -- its non-existence must win as a 404 over a 400
        // for an invalid request body, so it is checked before validating EnvironmentId below.
        var scenario = await scenarioRepository.GetByIdAsync(organizationId, id);
        if (scenario is null)
        {
            return NotFound();
        }

        var environment = await environmentRepository.GetByIdAsync(
            organizationId,
            request.EnvironmentId
        );
        if (environment is null || environment.ApplicationId != scenario.ApplicationId)
        {
            return BadRequest(
                new ErrorResponse(
                    "EnvironmentId does not reference an Environment belonging to this Scenario's "
                        + "Application."
                )
            );
        }

        return await RunsController.CreateRunAsync(
            organizationId,
            scenario.ApplicationId,
            ModelRunTrigger.Authoring,
            environment,
            [scenario],
            runSnapshotBuilder,
            runRepository,
            clock,
            User.GetUserId()
        );
    }
}
