using A2.Server.Common;
using A2.Server.Contracts;
using A2.Server.Repositories;
using A2.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Precondition = A2.Server.Models.Precondition;

namespace A2.Server.Controllers;

[ApiController]
[Authorize]
public class PreconditionsController(
    IPreconditionRepository preconditionRepository,
    ICallerOrganizationService callerOrganizationService,
    IClock clock
) : ControllerBase
{
    /// <response code="404">No Application with the given appId exists in the caller's Organization.</response>
    [HttpPost("applications/{appId:guid}/preconditions", Name = "CreatePrecondition")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PreconditionResponse>> Create(
        Guid appId,
        CreatePreconditionRequest request
    )
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var userId = User.GetUserId();
        var now = clock.UtcNow;
        var precondition = new Precondition
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ApplicationId = appId,
            Name = request.Name,
            ValueSource = request.ValueSource.ToModel(),
            ExampleValue = request.ExampleValue,
            CreatedByUserId = userId,
            UpdatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // Application existence is checked here via the save's condition expression instead of a
        // separate lookup, so there's no gap for the app to be deleted in between.
        if (!await preconditionRepository.TrySaveAsync(precondition))
        {
            return NotFound();
        }
        return Ok(precondition.ToResponse());
    }

    [HttpGet("applications/{appId:guid}/preconditions", Name = "ListPreconditions")]
    public async Task<ActionResult<IReadOnlyList<PreconditionResponse>>> List(Guid appId)
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var preconditions = await preconditionRepository.ListByApplicationAsync(
            organizationId,
            appId
        );
        return Ok(preconditions.Select(p => p.ToResponse()).ToList());
    }

    /// <response code="404">No Precondition with the given id exists in the caller's Organization.</response>
    [HttpPatch("preconditions/{id:guid}", Name = "UpdatePrecondition")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PreconditionResponse>> Update(
        Guid id,
        UpdatePreconditionRequest request
    )
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var existing = await preconditionRepository.GetByIdAsync(organizationId, id);
        if (existing is null)
        {
            return NotFound();
        }

        var fields = new PreconditionUpdatableFields
        {
            Name = request.Name,
            ValueSource = request.ValueSource.ToModel(),
            ExampleValue = request.ExampleValue,
            UpdatedByUserId = User.GetUserId(),
            UpdatedAt = clock.UtcNow,
        };
        var updateSucceeded = await preconditionRepository.TryUpdateAsync(
            organizationId,
            existing.ApplicationId,
            id,
            fields
        );
        if (!updateSucceeded)
        {
            return NotFound();
        }
        var updated = existing with
        {
            Name = fields.Name,
            ValueSource = fields.ValueSource,
            ExampleValue = fields.ExampleValue,
            UpdatedByUserId = fields.UpdatedByUserId,
            UpdatedAt = fields.UpdatedAt,
        };
        return Ok(updated.ToResponse());
    }

    [HttpDelete("preconditions/{id:guid}", Name = "DeletePrecondition")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> Delete(Guid id)
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        await preconditionRepository.DeleteAsync(organizationId, id);
        return NoContent();
    }
}
