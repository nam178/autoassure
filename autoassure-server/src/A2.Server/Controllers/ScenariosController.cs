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
    IActivityRepository activityRepository,
    ICallerOrganizationService callerOrganizationService,
    IClock clock
) : ControllerBase
{
    private const int MaxTagLength = 50;
    private const string DefaultFolder = "/";

    /// <response code="400">A tag in Tags is longer than 50 characters.</response>
    /// <response code="404">No Application with the given appId exists in the caller's Organization,
    /// or it no longer exists (deleted after this request started).</response>
    [HttpPost("applications/{appId:guid}/scenarios", Name = "CreateScenario")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ScenarioResponse>> Create(
        Guid appId,
        CreateScenarioRequest request
    )
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();

        // The Application is the URL resource -- its non-existence must win as a 404 over a 400 for
        // an invalid request body, so it's checked before validating tags below.
        if (await applicationRepository.GetByIdAsync(organizationId, appId) is null)
        {
            return NotFound();
        }

        var tags = request.Tags ?? [];
        if (!TryValidateTags(tags, out var tagsError))
        {
            return BadRequest(new ErrorResponse(tagsError!));
        }

        var userId = User.GetUserId();
        var now = clock.UtcNow;
        var scenario = new Scenario
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ApplicationId = appId,
            Title = request.Title,
            Description = request.Description,
            Folder = string.IsNullOrEmpty(request.Folder) ? DefaultFolder : request.Folder,
            Tags = tags,
            ActivityCount = 0,
            CreatedByUserId = userId,
            UpdatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // Application existence is checked here via the save's condition expression instead of a
        // separate lookup, so there's no gap for the app to be deleted in between.
        var result = await scenarioRepository.TrySaveAsync(scenario);
        return result switch
        {
            ScenarioWriteResult.Success => Ok(scenario.ToResponse()),
            _ => NotFound(),
        };
    }

    /// <response code="400">Both folder and tag were provided; they are mutually exclusive.</response>
    [HttpGet("applications/{appId:guid}/scenarios", Name = "ListScenarios")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<ScenarioResponse>>> List(
        Guid appId,
        [FromQuery] string? folder,
        [FromQuery] string? tag
    )
    {
        if (!string.IsNullOrEmpty(folder) && !string.IsNullOrEmpty(tag))
        {
            return BadRequest(new ErrorResponse("folder and tag are mutually exclusive."));
        }

        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var scenarios =
            !string.IsNullOrEmpty(folder)
                ? await scenarioRepository.ListByFolderAsync(organizationId, appId, folder)
            : !string.IsNullOrEmpty(tag)
                ? await scenarioRepository.ListByTagAsync(organizationId, appId, tag)
            : await scenarioRepository.ListByApplicationAsync(organizationId, appId);

        return Ok(scenarios.Select(s => s.ToResponse()).ToList());
    }

    /// <response code="404">No Scenario with the given id exists in the caller's Organization.</response>
    [HttpGet("scenarios/{id:guid}", Name = "GetScenarioById")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ScenarioResponse>> GetById(Guid id)
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var scenario = await scenarioRepository.GetByIdAsync(organizationId, id);
        return scenario is null ? NotFound() : Ok(scenario.ToResponse());
    }

    /// <response code="400">A tag in Tags is longer than 50 characters.</response>
    /// <response code="404">No Scenario with the given id exists in the caller's Organization, or its
    /// Application no longer exists (deleted after this request started).</response>
    [HttpPatch("scenarios/{id:guid}", Name = "UpdateScenario")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ScenarioResponse>> Update(Guid id, UpdateScenarioRequest request)
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var previous = await scenarioRepository.GetByIdAsync(organizationId, id);
        if (previous is null)
        {
            return NotFound();
        }

        var tags = request.Tags ?? [];
        if (!TryValidateTags(tags, out var tagsError))
        {
            return BadRequest(new ErrorResponse(tagsError!));
        }

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
            ScenarioWriteResult.Success => Ok(updated.ToResponse()),
            _ => NotFound(),
        };
    }

    /// <response code="404">No Scenario with the given id exists in the caller's Organization.</response>
    [HttpDelete("scenarios/{id:guid}", Name = "DeleteScenario")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(Guid id)
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var scenario = await scenarioRepository.GetByIdAsync(organizationId, id);
        if (scenario is null)
        {
            return NotFound();
        }

        // When a Scenario is deleted, then its Activities must be removed first -- otherwise deleted
        // Scenarios would leave orphaned Activity rows behind.
        await activityRepository.DeleteAllByScenarioAsync(organizationId, id);
        await scenarioRepository.DeleteAsync(scenario);
        return NoContent();
    }

    private static bool TryValidateTags(IReadOnlyList<string> tags, out string? error)
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
