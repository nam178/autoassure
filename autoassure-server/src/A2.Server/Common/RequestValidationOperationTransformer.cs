using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace A2.Server.Common;

/// <summary>
///     Documents that an endpoint returns 400 when its request has a property
///     carrying ANY
///     <see cref="ValidationAttribute" /> (e.g. <see cref="NotBlankAttribute" />,
///     <c>[Required]</c>,
///     <c>[MaxLength]</c>), so the constraint is reflected in the response docs
///     even for endpoints
///     whose XML comments don't already describe a 400.
/// </summary>
public class RequestValidationOperationTransformer
    : IOpenApiOperationTransformer
{
    private const string Note =
        "Returns 400 when the request fails a validation constraint.";

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        var hasValidatedParam =
            context.Description.ParameterDescriptions.Any(p =>
                p.ModelMetadata.ValidatorMetadata.OfType<ValidationAttribute>()
                    .Any()
            );
        if (!hasValidatedParam) return Task.CompletedTask;

        operation.Responses ??= [];
        var response = operation.Responses.GetValueOrDefault("400");
        if (response is null)
            operation.Responses["400"] = new OpenApiResponse
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