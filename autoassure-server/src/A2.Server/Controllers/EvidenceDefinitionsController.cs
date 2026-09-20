using A2.Server.Common;
using A2.Server.Contracts;
using A2.Server.Repositories;
using A2.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EvidenceDefinition = A2.Server.Models.EvidenceDefinition;

namespace A2.Server.Controllers;

[ApiController]
[Authorize]
public class EvidenceDefinitionsController(
    IEvidenceDefinitionRepository evidenceDefinitionRepository,
    ICallerOrganizationService callerOrganizationService,
    IClock clock
) : ControllerBase
{
    /// <response code="404">
    ///     No Application with the given applicationId exists in the
    ///     caller's Organization.
    /// </response>
    [HttpPost(
        "applications/{applicationId:guid}/evidence-definitions",
        Name = "CreateEvidenceDefinition"
    )]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EvidenceDefinitionResponse>> Create(
        Guid applicationId,
        CreateEvidenceDefinitionRequest request
    )
    {
        var callerOrganization =
            await callerOrganizationService.GetCallerOrganizationAsync();
        var organizationId = callerOrganization.Id;
        var userId = User.GetUserId();
        var now = clock.UtcNow;
        var evidence = new EvidenceDefinition
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            Name = request.Name,
            Description = request.Description,
            ExampleValue = request.ExampleValue,
            CreatedByUserId = userId,
            UpdatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // Application existence is checked here via the save's condition expression instead of a
        // separate lookup, so there's no gap for the app to be deleted in between.
        if (!await evidenceDefinitionRepository.TrySaveAsync(evidence))
            return NotFound();
        return Ok(evidence.ToResponse());
    }

    [HttpGet(
        "applications/{applicationId:guid}/evidence-definitions",
        Name = "ListEvidenceDefinitions"
    )]
    public async Task<
        ActionResult<IReadOnlyList<EvidenceDefinitionResponse>>
    > List(Guid applicationId)
    {
        var callerOrganization =
            await callerOrganizationService.GetCallerOrganizationAsync();
        var organizationId = callerOrganization.Id;
        var evidenceDefinitions =
            await evidenceDefinitionRepository.ListByApplicationAsync(
                organizationId,
                applicationId
            );
        return Ok(evidenceDefinitions.Select(e => e.ToResponse()).ToList());
    }

    /// <response code="404">
    ///     No EvidenceDefinition with the given evidenceDefinitionId exists in the
    ///     caller's Organization.
    /// </response>
    [HttpPatch(
        "evidence-definitions/{evidenceDefinitionId:guid}",
        Name = "UpdateEvidenceDefinition"
    )]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EvidenceDefinitionResponse>> Update(
        Guid evidenceDefinitionId,
        UpdateEvidenceDefinitionRequest request
    )
    {
        var callerOrganization =
            await callerOrganizationService.GetCallerOrganizationAsync();
        var organizationId = callerOrganization.Id;
        var existing = await evidenceDefinitionRepository.GetByIdAsync(
            organizationId,
            evidenceDefinitionId
        );
        if (existing is null) return NotFound();

        var fields = new EvidenceDefinitionUpdatableFields
        {
            Name = request.Name,
            Description = request.Description,
            ExampleValue = request.ExampleValue,
            UpdatedByUserId = User.GetUserId(),
            UpdatedAt = clock.UtcNow,
        };
        var updateSucceeded = await evidenceDefinitionRepository.TryUpdateAsync(
            organizationId,
            existing.ApplicationId,
            evidenceDefinitionId,
            fields
        );
        if (!updateSucceeded) return NotFound();
        var updated = existing with
        {
            Name = fields.Name,
            Description = fields.Description,
            ExampleValue = fields.ExampleValue,
            UpdatedByUserId = fields.UpdatedByUserId,
            UpdatedAt = fields.UpdatedAt,
        };
        return Ok(updated.ToResponse());
    }
}