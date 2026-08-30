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
    IApplicationRepository applicationRepository,
    IEvidenceDefinitionRepository evidenceDefinitionRepository,
    ICallerOrganizationService callerOrganizationService,
    IClock clock
) : ControllerBase
{
    /// <response code="404">No Application with the given appId exists in the caller's Organization.</response>
    [HttpPost("applications/{appId:guid}/evidence-definitions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EvidenceDefinitionResponse>> Create(
        Guid appId,
        CreateEvidenceDefinitionRequest request
    )
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        if (await applicationRepository.GetByIdAsync(organizationId, appId) is null)
        {
            return NotFound();
        }

        var userId = User.GetUserId();
        var now = clock.UtcNow;
        var evidence = new EvidenceDefinition
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ApplicationId = appId,
            Name = request.Name,
            Description = request.Description,
            ExampleValue = request.ExampleValue,
            CreatedByUserId = userId,
            UpdatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        if (!await evidenceDefinitionRepository.TrySaveAsync(evidence))
        {
            return NotFound();
        }
        return Ok(evidence.ToResponse());
    }

    [HttpGet("applications/{appId:guid}/evidence-definitions")]
    public async Task<ActionResult<IReadOnlyList<EvidenceDefinitionResponse>>> List(Guid appId)
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var evidenceDefinitions = await evidenceDefinitionRepository.ListByApplicationAsync(
            organizationId,
            appId
        );
        return Ok(evidenceDefinitions.Select(e => e.ToResponse()).ToList());
    }

    /// <response code="404">No EvidenceDefinition with the given id exists in the caller's Organization.</response>
    [HttpPatch("evidence-definitions/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EvidenceDefinitionResponse>> Update(
        Guid id,
        UpdateEvidenceDefinitionRequest request
    )
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var existing = await evidenceDefinitionRepository.GetByIdAsync(organizationId, id);
        if (existing is null)
        {
            return NotFound();
        }

        var fields = new EvidenceDefinitionUpdatableFields
        {
            Name = request.Name,
            Description = request.Description,
            ExampleValue = request.ExampleValue,
            UpdatedByUserId = User.GetUserId(),
            UpdatedAt = clock.UtcNow,
        };
        await evidenceDefinitionRepository.UpdateAsync(
            organizationId,
            existing.ApplicationId,
            id,
            fields
        );
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

    [HttpDelete("evidence-definitions/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> Delete(Guid id)
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        await evidenceDefinitionRepository.DeleteAsync(organizationId, id);
        return NoContent();
    }
}
