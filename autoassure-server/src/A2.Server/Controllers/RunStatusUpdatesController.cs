using A2.Server.Common;
using A2.Server.Contracts;
using A2.Server.Repositories;
using A2.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace A2.Server.Controllers;

/// <summary>A Run's append-only status update log: Append Run Status Update (one entry, at the caller's
/// own sequence number) and List Run Status Updates (the polling endpoint a client folds). Neither
/// endpoint interprets, derives Run state from, or returns a reconstruction of these rows -- see
/// fix_run_design.md section 7.</summary>
[ApiController]
[Authorize]
public class RunStatusUpdatesController(
    IRunRepository runRepository,
    ICallerOrganizationService callerOrganizationService,
    IClock clock
) : ControllerBase
{
    // How many rows one List Run Status Updates call returns at most. This is the public, client-driven
    // cursor fix_run_design.md section 4 describes -- a caller wanting more polls again with the last Seq
    // it received, rather than this endpoint looping to assemble a complete result.
    private const int MaxStatusUpdatesPerPage = 1000;

    /// <response code="400">ActivityResult.Status is Pending or Running.</response>
    /// <response code="404">No Run with the given runId exists in this Application, in the caller's
    /// Organization.</response>
    /// <response code="409">Seq is not greater than the Run's current LastSeq, or the Run's Status is not
    /// Running.</response>
    [HttpPost(
        "applications/{appId:guid}/runs/{runId:guid}/status-updates",
        Name = "AppendRunStatusUpdate"
    )]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RunStatusUpdateResponse>> Append(
        Guid appId,
        Guid runId,
        AppendRunStatusUpdateRequest request
    )
    {
        if (
            request.ActivityResult.Status == ActivityResultStatus.Pending
            || request.ActivityResult.Status == ActivityResultStatus.Running
        )
        {
            return BadRequest(
                new ErrorResponse("ActivityResult.Status must be Passed, Failed or Skipped.")
            );
        }

        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();

        // This Get's only purpose is answering whether the Run exists at all, which the append
        // transaction's own conditions cannot distinguish from "exists but not Running".
        var run = await runRepository.GetByIdAsync(organizationId, appId, runId);
        if (run is null)
        {
            return NotFound();
        }

        var update = request.ToModel(clock.UtcNow);
        var appended = await runRepository.TryAppendStatusUpdateAsync(
            organizationId,
            appId,
            runId,
            update
        );
        return appended
            ? Ok(update.ToResponse())
            : Conflict(
                new ErrorResponse("Seq has already been used, or the Run's Status is not Running.")
            );
    }

    /// <param name="after">Return only updates with a higher Seq than this. Pass 0 (the default) to read
    /// from the start of the log.</param>
    /// <response code="400">after is negative.</response>
    [HttpGet(
        "applications/{appId:guid}/runs/{runId:guid}/status-updates",
        Name = "ListRunStatusUpdates"
    )]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<RunStatusUpdateResponse>>> List(
        Guid appId,
        Guid runId,
        [FromQuery] long after = 0
    )
    {
        if (after < 0)
        {
            return BadRequest(new ErrorResponse("after must not be negative."));
        }

        // Deliberately does not Get the Run first: that would re-read its Scenario snapshots on every
        // poll, which is exactly the cost the split into header/snapshot/update rows exists to avoid (see
        // fix_run_design.md section 3). A Run that does not exist, or belongs to a different Application,
        // simply has no update rows at this key, so this returns an empty list rather than 404 -- the
        // client already learned the Run exists from Create Run or Get Run before it ever polls here.
        var organizationId = await callerOrganizationService.GetOrganizationIdAsync();
        var updates = await runRepository.ListStatusUpdatesAsync(
            organizationId,
            appId,
            runId,
            after,
            MaxStatusUpdatesPerPage
        );
        return Ok(updates.Select(u => u.ToResponse()).ToList());
    }
}
