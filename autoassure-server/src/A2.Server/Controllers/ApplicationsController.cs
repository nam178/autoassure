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
[Route("applications")]
public class ApplicationsController(
    IApplicationRepository applicationRepository,
    ICallerOrganizationService callerOrganizationService,
    IClock clock
) : ControllerBase
{
    /// <response code="400">The caller's Organization could not be found or has been deleted.</response>
    [HttpPost(Name = "CreateApplication")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApplicationResponse>> Create(CreateApplicationRequest request)
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var userId = User.GetUserId();
        var now = clock.UtcNow;
        var application = new Application
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            Name = request.Name,
            Description = request.Description,
            CreatedByUserId = userId,
            UpdatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // Organization not existing is not the client-facing "resource" they asked for -- a 404
        // would misleadingly imply the /applications route itself doesn't resolve.
        if (!await applicationRepository.TrySaveAsync(application))
        {
            return BadRequest(
                new ErrorResponse("Organization could not be found or has been deleted.")
            );
        }

        return Ok(application.ToResponse());
    }

    [HttpGet(Name = "ListApplications")]
    public async Task<ActionResult<IReadOnlyList<ApplicationResponse>>> List()
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var applications = await applicationRepository.ListByOrganizationAsync(organizationId);

        return Ok(applications.Select(a => a.ToResponse()).ToList());
    }

    /// <response code="404">No Application with the given id exists in the caller's Organization.</response>
    [HttpGet("{id:guid}", Name = "GetApplicationById")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApplicationResponse>> GetById(Guid id)
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var application = await applicationRepository.GetByIdAsync(organizationId, id);

        return application is null ? NotFound() : Ok(application.ToResponse());
    }
}
