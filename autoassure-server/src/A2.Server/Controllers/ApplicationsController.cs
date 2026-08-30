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
    /// <response code="404">The caller's Organization no longer exists.</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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

        if (!await applicationRepository.TrySaveAsync(application))
        {
            return NotFound();
        }

        return Ok(application.ToResponse());
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ApplicationResponse>>> List()
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var applications = await applicationRepository.ListByOrganizationAsync(organizationId);

        return Ok(applications.Select(a => a.ToResponse()).ToList());
    }

    /// <response code="404">No Application with the given id exists in the caller's Organization.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApplicationResponse>> GetById(Guid id)
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var application = await applicationRepository.GetByIdAsync(organizationId, id);

        return application is null ? NotFound() : Ok(application.ToResponse());
    }
}
