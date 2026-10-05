using System.Diagnostics;
using A2.Server.Common;
using A2.Server.Engine;
using A2.Server.Engine.Contracts;
using A2.Server.Engine.Repositories;
using A2.Server.WebApi.Contracts;
using A2.Server.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Activity = A2.Server.Engine.Models.Activity;

namespace A2.Server.WebApi.Controllers;

[ApiController]
[Authorize]
public class ActivitiesController(
    IScenarioRepository scenarioRepository,
    IActivityRepository activityRepository,
    ICallerOrganizationService callerOrganizationService,
    IClock clock
) : ControllerBase
{
    /// <response code="400">
    /// Order is outside 0..89, PreconditionIds/EvidenceIds do not reference
    /// existing library rows in the Scenario's Application, or the Scenario
    /// already has the maximum number of Activities (90).
    /// </response>
    /// <response code="404">
    /// No Scenario with the given scenarioId exists
    /// </response>
    /// <response code="409">
    /// Scenario exists but is not active (archived or deleting).
    /// </response>
    [HttpPost(
        "applications/{applicationId:guid}/scenarios/{scenarioId:guid}/activities",
        Name = "CreateActivity"
    )]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ErrorResponse),
        StatusCodes.Status400BadRequest
    )]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ActivityResponse>> Create(
        Guid applicationId,
        Guid scenarioId,
        CreateActivityRequest request
    )
    {
        var callerOrganization =
            await callerOrganizationService.GetCallerOrganizationAsync();
        var organizationId = callerOrganization.Id;

        var userId = User.GetUserId();
        var now = clock.UtcNow;
        var activity = new Activity
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            ScenarioId = scenarioId,
            Description = request.Description,
            Order = request.Order,
            PreconditionIds = request.PreconditionIds ?? [],
            EvidenceIds = request.EvidenceIds ?? [],
            CreatedByUserId = userId,
            UpdatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // The Scenario Activity limit is enforced atomically inside TrySaveAsync against the live
        // ActivityCount on the Scenario row, so concurrent Creates can't bypass it.
        var result = await activityRepository.TrySaveAsync(activity);
        return result switch
        {
            ActivitySaveResult.Success => Ok(activity.ToResponse()),
            ActivitySaveResult.ScenarioNotFound => NotFound(),
            ActivitySaveResult.ScenarioNotActive => Conflict(
                new ErrorResponse("Scenario is not active.")
            ),
            ActivitySaveResult.ScenarioActivityLimitReached => BadRequest(
                new ErrorResponse(
                    $"A Scenario can have at most {Quota.MaxActivityCountPerScenario} Activities."
                )
            ),
            ActivitySaveResult.PreconditionOrEvidenceNotFound => BadRequest(
                new ErrorResponse(
                    "PreconditionIds/EvidenceIds must reference existing library rows."
                )
            ),
            _ => throw new UnreachableException(
                $"Unhandled {nameof(ActivitySaveResult)}: {result}"
            ),
        };
    }

    /// <response code="404">
    /// No Scenario with the given scenarioId exists in this Application,
    /// or the Application does not exist in the caller's Organization.
    /// </response>
    [HttpGet(
        "applications/{applicationId:guid}/scenarios/{scenarioId:guid}/activities",
        Name = "ListActivities"
    )]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ActivityResponse>>> List(
        Guid applicationId,
        Guid scenarioId
    )
    {
        var callerOrganization =
            await callerOrganizationService.GetCallerOrganizationAsync();
        var organizationId = callerOrganization.Id;
        if (
            await scenarioRepository.GetByIdAsync(
                organizationId,
                applicationId,
                scenarioId
            )
            is null
        )
            return NotFound();

        var activities = await activityRepository.ListByScenarioAsync(
            organizationId,
            scenarioId
        );
        return Ok(activities.Select(a => a.ToResponse()).ToList());
    }

    /// <response code="400">
    /// PreconditionIds/EvidenceIds do not reference existing library rows in
    /// the Scenario's Application.
    /// </response>
    /// <response code="404">
    /// No Activity with the given activityId exists in this Scenario,
    /// or the Scenario does not exist in this Application,
    /// or the Application does not exist in the caller's Organization.
    /// </response>
    [HttpPatch(
        "applications/{applicationId:guid}/scenarios/{scenarioId:guid}/activities/{activityId:guid}",
        Name = "UpdateActivity"
    )]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ErrorResponse),
        StatusCodes.Status400BadRequest
    )]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ActivityResponse>> Update(
        Guid applicationId,
        Guid scenarioId,
        Guid activityId,
        UpdateActivityRequest request
    )
    {
        var callerOrganization =
            await callerOrganizationService.GetCallerOrganizationAsync();
        var organizationId = callerOrganization.Id;
        var existing = await activityRepository.GetByIdAsync(
            organizationId,
            scenarioId,
            activityId
        );
        if (existing is null || existing.ApplicationId != applicationId)
            return NotFound();

        var preconditionIds = request.PreconditionIds ?? [];
        var evidenceIds = request.EvidenceIds ?? [];

        var fields = new ActivityUpdatableFields
        {
            Description = request.Description,
            PreconditionIds = preconditionIds,
            EvidenceIds = evidenceIds,
            UpdatedByUserId = User.GetUserId(),
            UpdatedAt = clock.UtcNow,
        };
        var result = await activityRepository.TryUpdateAsync(
            organizationId,
            existing.ApplicationId,
            existing.ScenarioId,
            activityId,
            fields
        );
        ActionResult<ActivityResponse>? failure = result switch
        {
            ActivityUpdateResult.Success => null,
            ActivityUpdateResult.ActivityNotFound => NotFound(),
            ActivityUpdateResult.PreconditionOrEvidenceNotFound => BadRequest(
                new ErrorResponse(
                    "PreconditionIds/EvidenceIds must reference existing library rows."
                )
            ),
            _ => throw new UnreachableException(
                $"Unhandled {nameof(ActivityUpdateResult)}: {result}"
            ),
        };
        if (failure is not null)
            return failure;

        var updated = existing with
        {
            Description = fields.Description,
            PreconditionIds = fields.PreconditionIds,
            EvidenceIds = fields.EvidenceIds,
            UpdatedByUserId = fields.UpdatedByUserId,
            UpdatedAt = fields.UpdatedAt,
        };
        return Ok(updated.ToResponse());
    }

    /// <response code="409">
    /// OrderedActivityIds is not exactly a permutation of the Scenario's
    /// current Activity ids, because the caller's copy of the Scenario is
    /// stale.
    /// </response>
    /// <response code="404">
    /// No Scenario with the given scenarioId exists in this Application,
    /// or the Application does not exist in the caller's Organization.
    /// </response>
    [HttpPatch(
        "applications/{applicationId:guid}/scenarios/{scenarioId:guid}/activities/order",
        Name = "ReorderActivities"
    )]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ActivityResponse>>> Reorder(
        Guid applicationId,
        Guid scenarioId,
        ReorderActivitiesRequest request
    )
    {
        var callerOrganization =
            await callerOrganizationService.GetCallerOrganizationAsync();
        var organizationId = callerOrganization.Id;
        if (
            await scenarioRepository.GetByIdAsync(
                organizationId,
                applicationId,
                scenarioId
            )
            is null
        )
            return NotFound();

        var current = await activityRepository.ListByScenarioAsync(
            organizationId,
            scenarioId
        );
        // When the given ids don't match the Scenario's current Activity set exactly, then reject
        // the request -- a partial or foreign set would otherwise silently drop/duplicate Activities.
        if (
            request.OrderedActivityIds.Count != current.Count
            || !request
                .OrderedActivityIds.ToHashSet()
                .SetEquals(current.Select(a => a.Id))
        )
            return Conflict(
                new ErrorResponse(
                    "OrderedActivityIds must be exactly a permutation of the Scenario's current Activity ids."
                )
            );

        var succeeded = await activityRepository.TryReorderAsync(
            organizationId,
            scenarioId,
            request.OrderedActivityIds
        );
        if (!succeeded)
            return NotFound();

        // The write only touches each Activity's Order field, so the response can be built from
        // `current` (fetched pre-write) plus the new order, avoiding a redundant re-fetch.
        var byId = current.ToDictionary(a => a.Id);
        var reordered = request.OrderedActivityIds.Select(
            (id, index) => byId[id] with { Order = index }
        );
        return Ok(reordered.Select(a => a.ToResponse()).ToList());
    }

    /// <response code="404">
    /// No Activity with the given activityId exists in this Scenario,
    /// or the Scenario does not exist in this Application,
    /// or the Application does not exist in the caller's Organization.
    /// </response>
    [HttpDelete(
        "applications/{applicationId:guid}/scenarios/{scenarioId:guid}/activities/{activityId:guid}",
        Name = "DeleteActivity"
    )]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteActivity(
        Guid applicationId,
        Guid scenarioId,
        Guid activityId
    )
    {
        var callerOrganization =
            await callerOrganizationService.GetCallerOrganizationAsync();
        var organizationId = callerOrganization.Id;
        var activity = await activityRepository.GetByIdAsync(
            organizationId,
            scenarioId,
            activityId
        );
        if (activity is null || activity.ApplicationId != applicationId)
            return NotFound();

        var succeeded = await activityRepository.TryDeleteAsync(
            organizationId,
            activity.ApplicationId,
            activity.ScenarioId,
            activityId
        );
        if (!succeeded)
            return NotFound();
        return NoContent();
    }
}
