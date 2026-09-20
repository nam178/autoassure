using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace A2.Server.Common;

/// <summary>
///     Documents that a non-GET operation on an authenticated endpoint returns 403
///     when the caller's
///     Organization is archived. The 403 describes the archived state,
///     automatically applying to all such
///     endpoints that don't carry
///     <see cref="AllowArchivedOrganizationAttribute" />. This reflects the
///     <see cref="RequireActiveOrganizationFilter" /> behavior in the response
///     docs, so API consumers know
///     that their archived organizations block writes.
/// </summary>
public class ArchivedOrganizationOperationTransformer
    : IOpenApiOperationTransformer
{
    private const string Note =
        "Returns 403 when the caller's Organization is archived.";

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        // Skip GET requests (they are always allowed to access archived organizations)
        if (
            context.Description.HttpMethod?.Equals(
                "GET",
                StringComparison.OrdinalIgnoreCase
            ) ?? false
        )
            return Task.CompletedTask;

        // Get the controller action descriptor to access attributes
        var controllerActionDescriptor =
            context.Description.ActionDescriptor as ControllerActionDescriptor;
        if (controllerActionDescriptor == null) return Task.CompletedTask;

        // Check if the endpoint requires authentication
        var methodAuthAttributes =
            controllerActionDescriptor.MethodInfo.GetCustomAttributes(
                typeof(AuthorizeAttribute),
                true
            );
        var hasAuthorize = methodAuthAttributes.Length > 0;
        if (!hasAuthorize)
        {
            // Also check controller-level authorization
            var controllerAuthAttributes =
                controllerActionDescriptor.ControllerTypeInfo
                    .GetCustomAttributes(
                        typeof(AuthorizeAttribute),
                        true
                    );
            hasAuthorize = controllerAuthAttributes.Length > 0;
            if (!hasAuthorize) return Task.CompletedTask;
        }

        // Check if the endpoint is marked to allow archived organizations
        var allowArchivedAttributes =
            controllerActionDescriptor.MethodInfo.GetCustomAttributes(
                typeof(AllowArchivedOrganizationAttribute),
                true
            );
        if (allowArchivedAttributes.Length > 0) return Task.CompletedTask;

        // Add or append the 403 response
        operation.Responses ??= [];
        var response = operation.Responses.GetValueOrDefault("403");
        if (response is null)
            operation.Responses["403"] = new OpenApiResponse
            {
                Description = Note,
            };
        else
            response.Description = string.IsNullOrEmpty(response.Description)
                ? Note
                : $"{response.Description} {Note}";

        return Task.CompletedTask;
    }
}