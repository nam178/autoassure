using System.Diagnostics;
using A2.Server.Common;
using A2.Server.Contracts;
using A2.Server.Models;
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

    /// <response code="400">
    /// A tag in Tags is longer than 50 characters or Tags contains duplicate values (case-sensitive).
    /// </response>
    /// <response code="404">
    /// No Application with the given applicationId exists in the caller's
    /// Organization,
    /// or it no longer exists (deleted after this request started).
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

    /// <summary>
    /// Returns active Scenarios in the Application. Supports filtering by folder or tag,
    /// which are mutually exclusive.
    /// </summary>
    /// <response code="400">
    /// Both folder and tag were provided; they are mutually
    /// exclusive.
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
        var result = await ListByLifecycleStateAsync(
            applicationId,
            folder,
            tag,
            LifecycleState.Active
        );
        return result;
    }

    /// <summary>Returns archived Scenarios in the Application. Supports filtering by folder or tag, which are mutually exclusive.</summary>
    /// <response code="400">
    /// Both folder and tag were provided; they are mutually
    /// exclusive.
    /// </response>
    [HttpGet(
        "applications/{applicationId:guid}/scenarios/archived",
        Name = "ListArchivedScenarios"
    )]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ErrorResponse),
        StatusCodes.Status400BadRequest
    )]
    public async Task<
        ActionResult<IReadOnlyList<ScenarioResponse>>
    > ListArchived(
        Guid applicationId,
        [FromQuery] string? folder,
        [FromQuery] string? tag
    )
    {
        var result = await ListByLifecycleStateAsync(
            applicationId,
            folder,
            tag,
            LifecycleState.Archived
        );
        return result;
    }

    private async Task<
        ActionResult<IReadOnlyList<ScenarioResponse>>
    > ListByLifecycleStateAsync(
        Guid applicationId,
        string? folder,
        string? tag,
        LifecycleState lifecycleState
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

        var filteredScenarios = scenarios
            .Where(s => s.LifecycleState == lifecycleState)
            .ToList();

        return Ok(filteredScenarios.Select(s => s.ToResponse()).ToList());
    }

    /// <response code="404">
    /// No Scenario with the given scenarioId exists in this Application,
    /// or the Application does not exist in the caller's Organization.
    /// </response>
    [HttpGet(
        "applications/{applicationId:guid}/scenarios/{scenarioId:guid}",
        Name = "GetScenarioById"
    )]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ScenarioResponse>> GetById(
        Guid applicationId,
        Guid scenarioId
    )
    {
        var callerOrganization =
            await callerOrganizationService.GetCallerOrganizationAsync();
        var organizationId = callerOrganization.Id;
        var scenario = await scenarioRepository.GetByIdAsync(
            organizationId,
            applicationId,
            scenarioId
        );
        return scenario is null ? NotFound() : Ok(scenario.ToResponse());
    }

    /// <response code="404">
    /// No Scenario with the given scenarioId exists in this Application,
    /// or the Application does not exist in the caller's Organization.
    /// </response>
    [HttpPost(
        "applications/{applicationId:guid}/scenarios/{scenarioId:guid}/archive",
        Name = "ArchiveScenario"
    )]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Archive(Guid applicationId, Guid scenarioId)
    {
        var callerOrganization =
            await callerOrganizationService.GetCallerOrganizationAsync();
        var organizationId = callerOrganization.Id;

        var success = await scenarioRepository.TrySetLifecycleStateAsync(
            organizationId,
            applicationId,
            scenarioId,
            LifecycleState.Archived
        );

        return success ? NoContent() : NotFound();
    }

    /// <response code="404">
    /// No Scenario with the given scenarioId exists in this Application,
    /// or the Application does not exist in the caller's Organization.
    /// </response>
    [HttpPost(
        "applications/{applicationId:guid}/scenarios/{scenarioId:guid}/unarchive",
        Name = "UnarchiveScenario"
    )]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Unarchive(
        Guid applicationId,
        Guid scenarioId
    )
    {
        var callerOrganization =
            await callerOrganizationService.GetCallerOrganizationAsync();
        var organizationId = callerOrganization.Id;

        var success = await scenarioRepository.TrySetLifecycleStateAsync(
            organizationId,
            applicationId,
            scenarioId,
            LifecycleState.Active
        );

        return success ? NoContent() : NotFound();
    }

    /// <response code="409">
    /// Someone else added or removed Activities on this Scenario while the
    /// delete was running. The caller should retry.
    /// </response>
    /// <response code="404">
    /// No Scenario with the given scenarioId exists in this Application,
    /// or the Application does not exist in the caller's Organization.
    /// </response>
    [HttpDelete(
        "applications/{applicationId:guid}/scenarios/{scenarioId:guid}",
        Name = "DeleteScenario"
    )]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Delete(Guid applicationId, Guid scenarioId)
    {
        var callerOrganization =
            await callerOrganizationService.GetCallerOrganizationAsync();
        var organizationId = callerOrganization.Id;

        var activities = await activityRepository.ListByScenarioAsync(
            organizationId,
            scenarioId
        );

        var result = await scenarioRepository.TryDeleteAsync(
            organizationId,
            applicationId,
            scenarioId,
            activities
        );

        return result switch
        {
            ScenarioDeleteResult.Success => NoContent(),
            ScenarioDeleteResult.ScenarioNotFound => NotFound(),
            ScenarioDeleteResult.ScenarioModifiedConcurrently => Conflict(
                new ErrorResponse(
                    "The Scenario was modified concurrently. Please retry."
                )
            ),
            _ => throw new UnreachableException(
                $"Unhandled {nameof(ScenarioDeleteResult)}: {result}"
            ),
        };
    }

    /// <response code="400">
    /// A tag in Tags is longer than 50 characters or Tags contains duplicate values (case-sensitive).
    /// </response>
    /// <response code="404">
    /// No Scenario with the given scenarioId exists in this Application,
    /// or the Application does not exist in the caller's Organization.
    /// </response>
    /// <response code="409">
    /// The Scenario's Application no longer exists (deleted after this request
    /// started).
    /// </response>
    [HttpPatch(
        "applications/{applicationId:guid}/scenarios/{scenarioId:guid}",
        Name = "UpdateScenario"
    )]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ErrorResponse),
        StatusCodes.Status400BadRequest
    )]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ScenarioResponse>> Update(
        Guid applicationId,
        Guid scenarioId,
        UpdateScenarioRequest request
    )
    {
        var callerOrganization =
            await callerOrganizationService.GetCallerOrganizationAsync();
        var organizationId = callerOrganization.Id;
        var previous = await scenarioRepository.GetByIdAsync(
            organizationId,
            applicationId,
            scenarioId
        );
        if (previous is null)
            return NotFound();

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

        var uniqueTags = new HashSet<string>(tags, StringComparer.Ordinal);
        if (uniqueTags.Count != tags.Count)
        {
            error = "tags must not contain duplicates (case-sensitive).";
            return false;
        }

        error = null;
        return true;
    }
}
