using System.Diagnostics;
using A2.Server.Common;
using A2.Server.Contracts;
using A2.Server.Repositories;
using A2.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Scenario = A2.Server.Models.Scenario;

namespace A2.Server.Controllers;

[ApiController]
[Authorize]
public class ScenariosController(
    IApplicationRepository applicationRepository,
    IScenarioRepository scenarioRepository,
    ICallerOrganizationService callerOrganizationService,
    IClock clock
) : ControllerBase
{
    private const int MaxTagLength = 50;
    private const string DefaultFolder = "/";

    /// <response code="400">A tag in Tags is longer than 50 characters.</response>
    /// <response code="404">
    ///     No Application with the given applicationId exists in the caller's
    ///     Organization,
    ///     or it no longer exists (deleted after this request started).
    /// </response>
    [HttpPost(
        "applications/{applicationId:guid}/scenarios",
        Name = "CreateScenario"
    )]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ErrorResponse),
        StatusCodes.Status400BadRequest
    )]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ScenarioResponse>> Create(
        Guid applicationId,
        CreateScenarioRequest request
    )
    {
        var callerOrganization =
            await callerOrganizationService.GetCallerOrganizationAsync();
        var organizationId = callerOrganization.Id;

        // The Application is the URL resource -- its non-existence must win as a 404 over a 400 for
        // an invalid request body, so it's checked before validating tags below.
        if (
            await applicationRepository.GetByIdAsync(
                organizationId,
                applicationId
            )
            is null
        )
            return NotFound();

        var tags = request.Tags ?? [];
        if (!TryValidateTags(tags, out var tagsError))
            return BadRequest(new ErrorResponse(tagsError!));

        var userId = User.GetUserId();
        var now = clock.UtcNow;
        var scenario = new Scenario
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            Title = request.Title,
            Description = request.Description,
            Folder = string.IsNullOrEmpty(request.Folder)
                ? DefaultFolder
                : request.Folder,
            Tags = tags,
            ActivityCount = 0,
            CreatedByUserId = userId,
            UpdatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // Application existence is checked here via the save's condition expression instead of a
        // separate lookup, so there's no gap for the app to be deleted in between.
        var success = await scenarioRepository.TrySaveAsync(scenario);
        return success ? Ok(scenario.ToResponse()) : NotFound();
    }

    /// <response code="400">
    ///     Both folder and tag were provided; they are mutually
    ///     exclusive.
    /// </response>
    [HttpGet(
        "applications/{applicationId:guid}/scenarios",
        Name = "ListScenarios"
    )]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ErrorResponse),
        StatusCodes.Status400BadRequest
    )]
    public async Task<ActionResult<IReadOnlyList<ScenarioResponse>>> List(
        Guid applicationId,
        [FromQuery] string? folder,
        [FromQuery] string? tag
    )
    {
        if (!string.IsNullOrEmpty(folder) && !string.IsNullOrEmpty(tag))
            return BadRequest(
                new ErrorResponse("folder and tag are mutually exclusive.")
            );

        var callerOrganization =
            await callerOrganizationService.GetCallerOrganizationAsync();
        var organizationId = callerOrganization.Id;
        var scenarios =
            !string.IsNullOrEmpty(folder)
                ? await scenarioRepository.ListByFolderAsync(
                    organizationId,
                    applicationId,
                    folder
                )
                : !string.IsNullOrEmpty(tag)
                    ? await scenarioRepository.ListByTagAsync(
                        organizationId,
                        applicationId,
                        tag
                    )
                    : await scenarioRepository.ListByApplicationAsync(
                        organizationId,
                        applicationId
                    );

        return Ok(scenarios.Select(s => s.ToResponse()).ToList());
    }

    /// <response code="404">
    ///     No Scenario with the given scenarioId exists in the caller's
    ///     Organization.
    /// </response>
    [HttpGet("scenarios/{scenarioId:guid}", Name = "GetScenarioById")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ScenarioResponse>> GetById(Guid scenarioId)
    {
        var callerOrganization =
            await callerOrganizationService.GetCallerOrganizationAsync();
        var organizationId = callerOrganization.Id;
        var scenario = await scenarioRepository.GetByIdAsync(
            organizationId,
            scenarioId
        );
        return scenario is null ? NotFound() : Ok(scenario.ToResponse());
    }

    /// <response code="400">A tag in Tags is longer than 50 characters.</response>
    /// <response code="404">
    ///     No Scenario with the given scenarioId exists in the caller's
    ///     Organization.
    /// </response>
    /// <response code="409">
    ///     The Scenario's Application no longer exists (deleted after this request
    ///     started).
    /// </response>
    [HttpPatch("scenarios/{scenarioId:guid}", Name = "UpdateScenario")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ErrorResponse),
        StatusCodes.Status400BadRequest
    )]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ScenarioResponse>> Update(
        Guid scenarioId,
        UpdateScenarioRequest request
    )
    {
        var callerOrganization =
            await callerOrganizationService.GetCallerOrganizationAsync();
        var organizationId = callerOrganization.Id;
        var previous = await scenarioRepository.GetByIdAsync(
            organizationId,
            scenarioId
        );
        if (previous is null) return NotFound();

        var tags = request.Tags ?? [];
        if (!TryValidateTags(tags, out var tagsError))
            return BadRequest(new ErrorResponse(tagsError!));

        var updated = previous with
        {
            Title = request.Title,
            Description = request.Description,
            Folder = request.Folder,
            Tags = tags,
            UpdatedByUserId = User.GetUserId(),
            UpdatedAt = clock.UtcNow,
        };

        // Scenario/Application existence is checked here via the update's condition expression
        // instead of a separate lookup, so there's no gap for either to be deleted in between.
        var result = await scenarioRepository.TryUpdateAsync(updated, previous);
        return result switch
        {
            ScenarioUpdateResult.Success => Ok(updated.ToResponse()),
            ScenarioUpdateResult.ApplicationNotFound => Conflict(
                new ErrorResponse(
                    "The Scenario's Application no longer exists."
                )
            ),
            ScenarioUpdateResult.ScenarioNotFound => NotFound(),
            _ => throw new UnreachableException(
                $"Unhandled {nameof(ScenarioUpdateResult)}: {result}"
            ),
        };
    }

    private static bool TryValidateTags(
        IReadOnlyList<string> tags,
        out string? error
    )
    {
        if (tags.Any(tag => tag.Length > MaxTagLength))
        {
            error = $"each tag must be at most {MaxTagLength} characters.";
            return false;
        }

        error = null;
        return true;
    }
}