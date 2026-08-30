using A2.Server.Common;
using A2.Server.Contracts;
using A2.Server.Repositories;
using A2.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Activity = A2.Server.Models.Activity;
using Scenario = A2.Server.Models.Scenario;

namespace A2.Server.Controllers;

[ApiController]
[Authorize]
public class ScenariosController(
    IApplicationRepository applicationRepository,
    IScenarioRepository scenarioRepository,
    IPreconditionRepository preconditionRepository,
    IEvidenceDefinitionRepository evidenceDefinitionRepository,
    ICallerOrganizationService callerOrganizationService,
    IClock clock
) : ControllerBase
{
    private const int MaxTagCount = 20;
    private const int MaxTagLength = 50;
    private const string DefaultFolder = "/";

    // Keeps a Scenario's TransactWriteItems well under DynamoDB's 100-item transaction limit: 1
    // (Application check) + 1 (Scenario put) + 1 (folder mapping) + up to MaxTagCount (tag mappings)
    // already uses up to 23 items, leaving headroom for reference checks.
    private const int MaxActivityReferenceCount = 50;

    /// <response code="400">Tags are invalid, an Activity's PreconditionIds/EvidenceIds do not reference existing library rows, or the total number of unique references exceeds the allowed maximum.</response>
    /// <response code="404">No Application with the given appId exists in the caller's Organization.</response>
    [HttpPost("applications/{appId:guid}/scenarios")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ScenarioResponse>> Create(
        Guid appId,
        CreateScenarioRequest request
    )
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        if (await applicationRepository.GetByIdAsync(organizationId, appId) is null)
        {
            return NotFound();
        }

        var tags = request.Tags ?? [];
        if (!TryValidateTags(tags, out var tagsError))
        {
            return BadRequest(new { error = tagsError });
        }

        var activities = await ToActivitiesAsync(organizationId, appId, request.Activities ?? []);
        if (activities is null)
        {
            return BadRequest(
                new { error = "PreconditionIds/EvidenceIds must reference existing library rows." }
            );
        }
        if (!TryValidateActivityReferenceCount(activities, out var referenceCountError))
        {
            return BadRequest(new { error = referenceCountError });
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
            Activities = activities,
            CreatedByUserId = userId,
            UpdatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var result = await scenarioRepository.TrySaveAsync(scenario);
        return result switch
        {
            ScenarioWriteResult.Success => Ok(scenario.ToResponse()),
            ScenarioWriteResult.ApplicationNotFound => NotFound(),
            _ => BadRequest(
                new { error = "PreconditionIds/EvidenceIds must reference existing library rows." }
            ),
        };
    }

    /// <response code="400">Both folder and tag were provided; they are mutually exclusive.</response>
    [HttpGet("applications/{appId:guid}/scenarios")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<ScenarioResponse>>> List(
        Guid appId,
        [FromQuery] string? folder,
        [FromQuery] string? tag
    )
    {
        if (!string.IsNullOrEmpty(folder) && !string.IsNullOrEmpty(tag))
        {
            return BadRequest(new { error = "folder and tag are mutually exclusive." });
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
    [HttpGet("scenarios/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ScenarioResponse>> GetById(Guid id)
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var scenario = await scenarioRepository.GetByIdAsync(organizationId, id);
        return scenario is null ? NotFound() : Ok(scenario.ToResponse());
    }

    /// <response code="400">Tags are invalid, an Activity's PreconditionIds/EvidenceIds do not reference existing library rows, or the total number of unique references exceeds the allowed maximum.</response>
    /// <response code="404">No Scenario with the given id exists in the caller's Organization.</response>
    [HttpPatch("scenarios/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
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
            return BadRequest(new { error = tagsError });
        }

        var activities = await ToActivitiesAsync(
            organizationId,
            previous.ApplicationId,
            request.Activities ?? []
        );
        if (activities is null)
        {
            return BadRequest(
                new { error = "PreconditionIds/EvidenceIds must reference existing library rows." }
            );
        }
        if (!TryValidateActivityReferenceCount(activities, out var referenceCountError))
        {
            return BadRequest(new { error = referenceCountError });
        }

        var updated = previous with
        {
            Title = request.Title,
            Description = request.Description,
            Folder = request.Folder,
            Tags = tags,
            Activities = activities,
            UpdatedByUserId = User.GetUserId(),
            UpdatedAt = clock.UtcNow,
        };

        var result = await scenarioRepository.TryUpdateAsync(updated, previous);
        return result switch
        {
            ScenarioWriteResult.Success => Ok(updated.ToResponse()),
            ScenarioWriteResult.ApplicationNotFound => NotFound(),
            _ => BadRequest(
                new { error = "PreconditionIds/EvidenceIds must reference existing library rows." }
            ),
        };
    }

    /// <response code="404">No Scenario with the given id exists in the caller's Organization.</response>
    [HttpDelete("scenarios/{id:guid}")]
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

        await scenarioRepository.DeleteAsync(scenario);
        return NoContent();
    }

    private static bool TryValidateTags(IReadOnlyList<string> tags, out string? error)
    {
        if (tags.Count > MaxTagCount)
        {
            error = $"tags must have at most {MaxTagCount} entries.";
            return false;
        }

        if (tags.Any(tag => tag.Length > MaxTagLength))
        {
            error = $"each tag must be at most {MaxTagLength} characters.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryValidateActivityReferenceCount(
        IReadOnlyList<Activity> activities,
        out string? error
    )
    {
        var uniqueReferenceCount =
            activities.SelectMany(a => a.PreconditionIds).Distinct().Count()
            + activities.SelectMany(a => a.EvidenceIds).Distinct().Count();
        if (uniqueReferenceCount > MaxActivityReferenceCount)
        {
            error =
                $"Activities must reference at most {MaxActivityReferenceCount} unique "
                + "Preconditions/EvidenceDefinitions in total.";
            return false;
        }

        error = null;
        return true;
    }

    // Validates that every PreconditionIds/EvidenceIds entry resolves to an existing library row in
    // the same Application, and returns the resulting domain Activities -- or null if any reference is invalid.
    private async Task<IReadOnlyList<Activity>?> ToActivitiesAsync(
        Guid organizationId,
        Guid applicationId,
        IReadOnlyList<ActivityRequest> requests
    )
    {
        var activities = new List<Activity>();
        foreach (var request in requests)
        {
            var preconditionIds = request.PreconditionIds ?? [];
            var evidenceIds = request.EvidenceIds ?? [];

            foreach (var preconditionId in preconditionIds)
            {
                var precondition = await preconditionRepository.GetByIdAsync(
                    organizationId,
                    preconditionId
                );
                if (precondition is null || precondition.ApplicationId != applicationId)
                {
                    return null;
                }
            }

            foreach (var evidenceId in evidenceIds)
            {
                var evidence = await evidenceDefinitionRepository.GetByIdAsync(
                    organizationId,
                    evidenceId
                );
                if (evidence is null || evidence.ApplicationId != applicationId)
                {
                    return null;
                }
            }

            activities.Add(
                new Activity
                {
                    Id = Guid.CreateVersion7(),
                    Description = request.Description,
                    PreconditionIds = preconditionIds,
                    EvidenceIds = evidenceIds,
                }
            );
        }

        return activities;
    }
}
