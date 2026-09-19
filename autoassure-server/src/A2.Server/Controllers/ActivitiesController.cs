using System.Diagnostics;
using A2.Server.Common;
using A2.Server.Contracts;
using A2.Server.Repositories;
using A2.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Activity = A2.Server.Models.Activity;

namespace A2.Server.Controllers;

[ApiController]
[Authorize]
public class ActivitiesController(
    IScenarioRepository scenarioRepository,
    IActivityRepository activityRepository,
    ICallerOrganizationService callerOrganizationService,
    IClock clock
) : ControllerBase
{
    /// <response code="400">PreconditionIds/EvidenceIds do not reference existing library rows in
    /// the Scenario's Application, or the Scenario already has the maximum number of
    /// Activities.</response>
    /// <response code="404">No Scenario with the given scenarioId exists in the caller's
    /// Organization, or it no longer exists (deleted after this request started).</response>
    [HttpPost("scenarios/{scenarioId:guid}/activities", Name = "CreateActivity")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ActivityResponse>> Create(
        Guid scenarioId,
        CreateActivityRequest request
    )
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();

        // The Scenario is the URL resource -- its non-existence must win as a 404 over a 400 for an
        // invalid request body, so it's checked before validating references below.
        var scenario = await scenarioRepository.GetByIdAsync(organizationId, scenarioId);
        if (scenario is null)
        {
            return NotFound();
        }

        var preconditionIds = request.PreconditionIds ?? [];
        var evidenceIds = request.EvidenceIds ?? [];

        var userId = User.GetUserId();
        var now = clock.UtcNow;
        var activity = new Activity
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ApplicationId = scenario.ApplicationId,
            ScenarioId = scenarioId,
            Description = request.Description,
            // The Scenario's ActivityCount (read above) reflects the count at the start of this
            // request, so a concurrent Create could assign the same Order -- harmless, since Order
            // only affects display sequence and can be fixed up via Reorder.
            Order = scenario.ActivityCount,
            PreconditionIds = preconditionIds,
            EvidenceIds = evidenceIds,
            CreatedByUserId = userId,
            UpdatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // The Scenario Activity limit is enforced atomically inside TrySaveAsync against the live
        // ActivityCount on the Scenario row, so it can't be bypassed by concurrent Creates racing
        // past the value read above.
        var result = await activityRepository.TrySaveAsync(activity);
        return result switch
        {
            ActivitySaveResult.Success => Ok(activity.ToResponse()),
            ActivitySaveResult.ScenarioNotFound => NotFound(),
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

    [HttpGet("scenarios/{scenarioId:guid}/activities", Name = "ListActivities")]
    public async Task<ActionResult<IReadOnlyList<ActivityResponse>>> List(Guid scenarioId)
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var activities = await activityRepository.ListByScenarioAsync(organizationId, scenarioId);
        return Ok(activities.Select(a => a.ToResponse()).ToList());
    }

    /// <response code="400">PreconditionIds/EvidenceIds do not reference existing library rows in
    /// the Scenario's Application.</response>
    /// <response code="404">No Activity with the given activityId exists in the caller's
    /// Organization.</response>
    [HttpPatch("activities/{activityId:guid}", Name = "UpdateActivity")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ActivityResponse>> Update(
        Guid activityId,
        UpdateActivityRequest request
    )
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var existing = await activityRepository.GetByIdAsync(organizationId, activityId);
        if (existing is null)
        {
            return NotFound();
        }

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
        {
            return failure;
        }

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

    /// <response code="400">OrderedActivityIds is not exactly a permutation of the Scenario's
    /// current Activity ids.</response>
    /// <response code="404">No Scenario with the given scenarioId exists in the caller's
    /// Organization.</response>
    [HttpPatch("scenarios/{scenarioId:guid}/activities/order", Name = "ReorderActivities")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ActivityResponse>>> Reorder(
        Guid scenarioId,
        ReorderActivitiesRequest request
    )
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        if (await scenarioRepository.GetByIdAsync(organizationId, scenarioId) is null)
        {
            return NotFound();
        }

        var current = await activityRepository.ListByScenarioAsync(organizationId, scenarioId);
        // When the given ids don't match the Scenario's current Activity set exactly, then reject
        // the request -- a partial or foreign set would otherwise silently drop/duplicate Activities.
        if (
            request.OrderedActivityIds.Count != current.Count
            || !request.OrderedActivityIds.ToHashSet().SetEquals(current.Select(a => a.Id))
        )
        {
            return BadRequest(
                new ErrorResponse(
                    "OrderedActivityIds must be exactly a permutation of the Scenario's current Activity ids."
                )
            );
        }

        var succeeded = await activityRepository.TryReorderAsync(
            organizationId,
            scenarioId,
            request.OrderedActivityIds
        );
        if (!succeeded)
        {
            return NotFound();
        }

        // The write only touches each Activity's Order field, so the response can be built from
        // `current` (fetched pre-write) plus the new order, avoiding a redundant re-fetch.
        var byId = current.ToDictionary(a => a.Id);
        var reordered = request.OrderedActivityIds.Select(
            (id, index) => byId[id] with { Order = index }
        );
        return Ok(reordered.Select(a => a.ToResponse()).ToList());
    }
}
