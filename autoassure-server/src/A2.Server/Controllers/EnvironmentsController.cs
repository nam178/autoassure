using System.Text.RegularExpressions;
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
public partial class EnvironmentsController(
    IEnvironmentRepository environmentRepository,
    IEnvironmentVariableRepository environmentVariableRepository,
    ICallerOrganizationService callerOrganizationService,
    IClock clock
) : ControllerBase
{
    private const int MaxKeyLength = 200;

    [GeneratedRegex("^[A-Za-z0-9_]+$")]
    private static partial Regex ValidKeyRegex();

    /// <response code="404">No Application with the given appId exists in the caller's Organization.</response>
    [HttpPost("applications/{appId:guid}/environments", Name = "CreateEnvironment")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EnvironmentResponse>> Create(
        Guid appId,
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
            ApplicationId = appId,
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

    [HttpGet("applications/{appId:guid}/environments", Name = "ListEnvironments")]
    public async Task<ActionResult<IReadOnlyList<EnvironmentResponse>>> List(Guid appId)
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var environments = await environmentRepository.ListByApplicationAsync(
            organizationId,
            appId
        );
        var responses = new List<EnvironmentResponse>(environments.Count);
        foreach (var environment in environments)
        {
            responses.Add(await ToResponseAsync(environment));
        }
        return Ok(responses);
    }

    /// <response code="404">No Environment with the given id exists in the caller's Organization.</response>
    [HttpGet("environments/{id:guid}", Name = "GetEnvironmentById")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EnvironmentResponse>> GetById(Guid id)
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var environment = await environmentRepository.GetByIdAsync(organizationId, id);
        if (environment is null)
        {
            return NotFound();
        }

        return Ok(await ToResponseAsync(environment));
    }

    /// <response code="404">No Environment with the given id exists in the caller's Organization.</response>
    [HttpPatch("environments/{id:guid}", Name = "UpdateEnvironment")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EnvironmentResponse>> Update(
        Guid id,
        UpdateEnvironmentRequest request
    )
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var existing = await environmentRepository.GetByIdAsync(organizationId, id);
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
            Classification = fields.Classification,
            UpdatedByUserId = fields.UpdatedByUserId,
            UpdatedAt = fields.UpdatedAt,
        };
        return Ok(await ToResponseAsync(updated));
    }

    /// <param name="key">Variable name. Must be 1-200 characters, using only letters, digits, and
    /// underscores.</param>
    /// <response code="400">key exceeds the maximum allowed length, or key contains characters other
    /// than letters, digits, or underscores.</response>
    /// <response code="404">No Environment with the given id exists in the caller's Organization, or it
    /// no longer exists (deleted after this request started).</response>
    [HttpPut("environments/{id:guid}/variables/{key}", Name = "SetEnvironmentVariable")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> SetVariable(
        Guid id,
        string key,
        SetEnvironmentVariableRequest request
    )
    {
        if (key.Length > MaxKeyLength)
        {
            return BadRequest(new ErrorResponse($"key must be at most {MaxKeyLength} characters."));
        }
        if (!ValidKeyRegex().IsMatch(key))
        {
            return BadRequest(
                new ErrorResponse("key must contain only letters, digits, and underscores.")
            );
        }

        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var environment = await environmentRepository.GetByIdAsync(organizationId, id);
        if (environment is null)
        {
            return NotFound();
        }

        // The Environment existed above but may have been deleted since -- still the same
        // client-facing resource the caller asked for, so this is a 404, same as the check above.
        if (
            !await environmentVariableRepository.TryUpdateAsync(
                organizationId,
                environment.ApplicationId,
                id,
                key,
                request.Value,
                User.GetUserId()
            )
        )
        {
            return NotFound();
        }
        return NoContent();
    }

    /// <response code="404">No Environment with the given id exists in the caller's Organization.</response>
    [HttpDelete("environments/{id:guid}/variables/{key}", Name = "DeleteEnvironmentVariable")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteVariable(Guid id, string key)
    {
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var environment = await environmentRepository.GetByIdAsync(organizationId, id);
        if (environment is null)
        {
            return NotFound();
        }

        await environmentVariableRepository.DeleteAsync(organizationId, id, key);
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
