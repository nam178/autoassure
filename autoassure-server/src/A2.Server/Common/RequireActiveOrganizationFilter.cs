using A2.Server.Contracts;
using A2.Server.Models;
using A2.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace A2.Server.Common;

/// <summary>
///     A global filter that blocks non-GET requests against archived or deleting
///     Organizations.
///     GET requests are always allowed. The filter returns 403 with a clear
///     message if the Organization
///     is not Active, except for endpoints marked with
///     <see cref="AllowArchivedOrganizationAttribute" />.
/// </summary>
public class RequireActiveOrganizationFilter(
    ICallerOrganizationService callerOrganizationService
) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next
    )
    {
        // Skip unauthenticated requests (e.g., Auth endpoints like sign-in)
        if (!context.HttpContext.User.Identity?.IsAuthenticated ?? false)
        {
            await next();
            return;
        }

        // GET requests are always allowed
        if (context.HttpContext.Request.Method == HttpMethods.Get)
        {
            await next();
            return;
        }

        // Check if the endpoint is marked to allow archived organizations
        var endpoint = context.HttpContext.GetEndpoint();
        var allowArchived =
            endpoint?.Metadata.GetMetadata<AllowArchivedOrganizationAttribute>();
        if (allowArchived is not null)
        {
            await next();
            return;
        }

        // For non-GET requests, check that the Organization is Active
        try
        {
            var organization =
                await callerOrganizationService.GetCallerOrganizationAsync();
            if (organization.LifecycleState != LifecycleState.Active)
            {
                context.Result = new ObjectResult(
                    new ErrorResponse(
                        "This organization is archived. Ask an owner to unarchive it."
                    )
                )
                {
                    StatusCode = StatusCodes.Status403Forbidden,
                };
                return;
            }
        }
        catch (InvalidOperationException ex)
            when (ex.Message.Contains("Organization"))
        {
            // If the organization doesn't exist, return 400 like the controller would
            context.Result = new ObjectResult(
                new ErrorResponse(
                    "Organization could not be found or has been deleted."
                )
            )
            {
                StatusCode = StatusCodes.Status400BadRequest,
            };
            return;
        }

        await next();
    }
}
