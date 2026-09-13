using System.ComponentModel.DataAnnotations;
using A2.Server.Common;
using A2.Server.Contracts;
using A2.Server.Repositories;
using A2.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Environment = A2.Server.Models.Environment;

namespace A2.Server.Controllers;

[ApiController]
[Authorize]
public class EnvironmentsController(
    IEnvironmentRepository environmentRepository,
    IEnvironmentVariableRepository environmentVariableRepository,
    ICallerOrganizationService callerOrganizationService,
    IClock clock
) : ControllerBase
{
    /// <response code="404">No Application with the given applicationId exists in the caller's Organization.</response>
    [HttpPost("applications/{applicationId:guid}/environments", Name = "CreateEnvironment")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EnvironmentResponse>> Create(
        Guid applicationId,
        CreateEnvironmentRequest request
    )
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var userId = User.GetUserId();
        var now = clock.UtcNow;

        var environment = new Environment
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ApplicationId = applicationId,
            Name = request.Name,
            Classification = request.Classification.ToModel(),
            CreatedByUserId = userId,
            UpdatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // The Application must exist in the caller's own Organization -- the repository enforces
        // this via a ConditionExpression, so a failed save means no such Application.
        if (!await environmentRepository.TrySaveAsync(environment))
        {
            return NotFound();
        }
        return Ok(await ToResponseAsync(environment));
    }

    [HttpGet("applications/{applicationId:guid}/environments", Name = "ListEnvironments")]
    public async Task<ActionResult<IReadOnlyList<EnvironmentResponse>>> List(Guid applicationId)
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var environments = await environmentRepository.ListByApplicationAsync(
            organizationId,
            applicationId
        );
        var responses = new List<EnvironmentResponse>(environments.Count);
        foreach (var environment in environments)
        {
            responses.Add(await ToResponseAsync(environment));
        }
        return Ok(responses);
    }

    /// <response code="404">No Environment with the given environmentId exists in the caller's
    /// Organization.</response>
    [HttpGet("environments/{environmentId:guid}", Name = "GetEnvironmentById")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EnvironmentResponse>> GetById(Guid environmentId)
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var environment = await environmentRepository.GetByIdAsync(organizationId, environmentId);
        if (environment is null)
        {
            return NotFound();
        }

        return Ok(await ToResponseAsync(environment));
    }

    /// <response code="404">No Environment with the given environmentId exists in the caller's
    /// Organization.</response>
    [HttpPatch("environments/{environmentId:guid}", Name = "UpdateEnvironment")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EnvironmentResponse>> Update(
        Guid environmentId,
        UpdateEnvironmentRequest request
    )
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var existing = await environmentRepository.GetByIdAsync(organizationId, environmentId);
        if (existing is null)
        {
            return NotFound();
        }

        var fields = new EnvironmentUpdatableFields
        {
            Name = request.Name,
            Classification = request.Classification.ToModel(),
            UpdatedByUserId = User.GetUserId(),
            UpdatedAt = clock.UtcNow,
        };
        var updateSucceeded = await environmentRepository.TryUpdateAsync(
            organizationId,
            existing.ApplicationId,
            environmentId,
            fields
        );
        if (!updateSucceeded)
        {
            return NotFound();
        }
        var updated = existing with
        {
            Name = fields.Name,
            Classification = fields.Classification,
            UpdatedByUserId = fields.UpdatedByUserId,
            UpdatedAt = fields.UpdatedAt,
        };
        return Ok(await ToResponseAsync(updated));
    }

    /// <param name="key">Variable name. Must be 1-200 characters, using only letters, digits, and
    /// underscores.</param>
    /// <response code="404">No Environment with the given environmentId exists in the caller's
    /// Organization, or it no longer exists (deleted after this request started).</response>
    [HttpPut("environments/{environmentId:guid}/variables/{key}", Name = "SetEnvironmentVariable")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> SetVariable(
        Guid environmentId,
        [MaxLength(200), RegularExpression("^[A-Za-z0-9_]+$")] string key,
        SetEnvironmentVariableRequest request
    )
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var environment = await environmentRepository.GetByIdAsync(organizationId, environmentId);
        if (environment is null)
        {
            return NotFound();
        }

        // The Environment existed above but may have been deleted since -- still the same
        // client-facing resource the caller asked for, so this is a 404, same as the check above.
        if (
            !await environmentVariableRepository.TrySaveAsync(
                organizationId,
                environment.ApplicationId,
                environmentId,
                key,
                request.Value,
                request.IsSensitive,
                User.GetUserId()
            )
        )
        {
            return NotFound();
        }
        return NoContent();
    }

    /// <response code="404">No Environment with the given environmentId exists in the caller's
    /// Organization.</response>
    [HttpDelete(
        "environments/{environmentId:guid}/variables/{key}",
        Name = "DeleteEnvironmentVariable"
    )]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteVariable(Guid environmentId, string key)
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var environment = await environmentRepository.GetByIdAsync(organizationId, environmentId);
        if (environment is null)
        {
            return NotFound();
        }

        await environmentVariableRepository.DeleteAsync(organizationId, environmentId, key);
        return NoContent();
    }

    private async Task<EnvironmentResponse> ToResponseAsync(Environment environment)
    {
        var variables = await environmentVariableRepository.ListByEnvironmentAsync(
            environment.OrganizationId,
            environment.Id
        );
        return environment.ToResponse(variables);
    }
}
