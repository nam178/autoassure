using System.Diagnostics;
using A2.Server.Common;
using A2.Server.Contracts;
using A2.Server.Models;
using A2.Server.Repositories;
using A2.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace A2.Server.Controllers;

[ApiController]
[Authorize]
[Route("organizations")]
public class OrganizationsController(
    IOrganizationRepository organizationRepository,
    IOrganizationUserRepository organizationUserRepository,
    ICallerOrganizationService callerOrganizationService
) : ControllerBase
{
    /// <response code="400">
    ///     The caller's Organization could not be found, has been
    ///     deleted, or is a personal organization.
    /// </response>
    /// <response code="403">The caller is not an Owner of the Organization.</response>
    /// <response code="404">The Organization does not exist.</response>
    [HttpPost("{id:guid}/archive", Name = "ArchiveOrganization")]
    [AllowArchivedOrganization]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(
        typeof(ErrorResponse),
        StatusCodes.Status400BadRequest
    )]
    [ProducesResponseType(
        typeof(ErrorResponse),
        StatusCodes.Status403Forbidden
    )]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Archive(Guid id)
    {
        var callerOrganization =
            await callerOrganizationService.GetCallerOrganizationAsync();
        var organizationId = callerOrganization.Id;
        var userId = User.GetUserId();

        // Reject if trying to archive a different organization
        if (id != organizationId)
            return BadRequest(
                new ErrorResponse(
                    "Organization could not be found or has been deleted."
                )
            );

        // Reject if it's a personal organization
        if (callerOrganization.IsPersonal)
            return BadRequest(
                new ErrorResponse("Personal organizations cannot be archived.")
            );

        // Check if the caller is an Owner
        var memberships = await organizationUserRepository.ListByUserAsync(
            userId
        );
        var membership = memberships.FirstOrDefault(m =>
            m.OrganizationId == organizationId
        );
        if (membership?.Role != OrganizationRole.Owner)
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new ErrorResponse("Only owners can archive this organization.")
            );

        // Archive the organization (idempotent)
        var updated = await organizationRepository.TrySetLifecycleStateAsync(
            organizationId,
            LifecycleState.Archived
        );

        return updated ? NoContent() : NotFound();
    }

    /// <response code="400">
    ///     The caller's Organization could not be found or has been
    ///     deleted.
    /// </response>
    /// <response code="403">The caller is not an Owner of the Organization.</response>
    /// <response code="404">The Organization does not exist.</response>
    [HttpPost("{id:guid}/unarchive", Name = "UnarchiveOrganization")]
    [AllowArchivedOrganization]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(
        typeof(ErrorResponse),
        StatusCodes.Status400BadRequest
    )]
    [ProducesResponseType(
        typeof(ErrorResponse),
        StatusCodes.Status403Forbidden
    )]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unarchive(Guid id)
    {
        var callerOrganization =
            await callerOrganizationService.GetCallerOrganizationAsync();
        var organizationId = callerOrganization.Id;
        var userId = User.GetUserId();

        // Reject if trying to unarchive a different organization
        if (id != organizationId)
            return BadRequest(
                new ErrorResponse(
                    "Organization could not be found or has been deleted."
                )
            );

        // Check if the caller is an Owner
        var memberships = await organizationUserRepository.ListByUserAsync(
            userId
        );
        var membership = memberships.FirstOrDefault(m =>
            m.OrganizationId == organizationId
        );
        if (membership?.Role != OrganizationRole.Owner)
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new ErrorResponse(
                    "Only owners can unarchive this organization."
                )
            );

        // Unarchive the organization (idempotent)
        var updated = await organizationRepository.TrySetLifecycleStateAsync(
            organizationId,
            LifecycleState.Active
        );

        return updated ? NoContent() : NotFound();
    }

    /// <summary>Returns the caller's active Organizations.</summary>
    [HttpGet(Name = "ListOrganizations")]
    public async Task<ActionResult<IReadOnlyList<OrganizationResponse>>> List()
    {
        var userId = User.GetUserId();
        var memberships = await organizationUserRepository.ListByUserAsync(
            userId
        );

        var organizationIds = memberships
            .Select(m => m.OrganizationId)
            .ToList();
        var allOrganizations = await organizationRepository.GetByIdsAsync(
            organizationIds
        );
        var activeOrganizations = allOrganizations
            .Where(o => o.LifecycleState == LifecycleState.Active)
            .ToList();

        return Ok(activeOrganizations.Select(o => o.ToResponse()).ToList());
    }

    /// <summary>Returns the caller's archived Organizations.</summary>
    [HttpGet("archived", Name = "ListArchivedOrganizations")]
    public async Task<
        ActionResult<IReadOnlyList<OrganizationResponse>>
    > ListArchived()
    {
        var userId = User.GetUserId();
        var memberships = await organizationUserRepository.ListByUserAsync(
            userId
        );

        var organizationIds = memberships
            .Select(m => m.OrganizationId)
            .ToList();
        var allOrganizations = await organizationRepository.GetByIdsAsync(
            organizationIds
        );
        var archivedOrganizations = allOrganizations
            .Where(o => o.LifecycleState == LifecycleState.Archived)
            .ToList();

        return Ok(archivedOrganizations.Select(o => o.ToResponse()).ToList());
    }

    /// <response code="400">
    ///     The target Organization is not the caller's own Organization, the
    ///     Organization is personal, or the caller is the last Owner.
    /// </response>
    /// <response code="403">The caller is not an Owner.</response>
    /// <response code="404">The target User is not a member of the Organization.</response>
    [HttpDelete(
        "{organizationId:guid}/members/{userId:guid}",
        Name = "RemoveOrganizationMember"
    )]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(
        typeof(ErrorResponse),
        StatusCodes.Status400BadRequest
    )]
    [ProducesResponseType(
        typeof(ErrorResponse),
        StatusCodes.Status403Forbidden
    )]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveOrganizationMember(
        Guid organizationId,
        Guid userId
    )
    {
        var callerOrganization =
            await callerOrganizationService.GetCallerOrganizationAsync();
        var callerId = User.GetUserId();

        if (organizationId != callerOrganization.Id)
            return BadRequest(
                new ErrorResponse(
                    "Organization could not be found or has been deleted."
                )
            );

        var callerMembership = await organizationUserRepository.GetAsync(
            callerOrganization.Id,
            callerId
        );
        if (callerMembership?.Role != OrganizationRole.Owner)
            return StatusCode(StatusCodes.Status403Forbidden);

        if (callerOrganization.IsPersonal)
            return BadRequest(
                new ErrorResponse(
                    "Cannot remove members from a personal Organization."
                )
            );

        var targetMembership = await organizationUserRepository.GetAsync(
            callerOrganization.Id,
            userId
        );
        if (targetMembership is null)
            return NotFound();

        var outcome = await organizationUserRepository.TryDeleteAsync(
            callerOrganization.Id,
            userId,
            targetMembership.Role
        );

        return outcome switch
        {
            RemoveMembershipOutcome.Removed => NoContent(),
            RemoveMembershipOutcome.NotFound => NotFound(),
            RemoveMembershipOutcome.CannotDeleteLastOwner => BadRequest(
                new ErrorResponse(
                    "You are the only owner. Make someone else an owner before leaving."
                )
            ),
            _ => throw new UnreachableException(
                $"Unhandled {nameof(RemoveMembershipOutcome)}: {outcome}"
            ),
        };
    }
}
