using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace A2.Server.Common;

/// <summary>
///     Appends a note to a Contract property's OpenAPI schema description when it
///     carries
///     <see cref="NotBlankAttribute" />, since that constraint isn't otherwise
///     expressible as a JSON
///     Schema keyword and would go undocumented in the generated spec/SDK.
/// </summary>
public class NotBlankSchemaTransformer : IOpenApiSchemaTransformer
{
    private const string Note = "Must not be empty or whitespace.";

    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        var hasNotBlank =
            context
                .JsonPropertyInfo?.AttributeProvider?.GetCustomAttributes(
                    typeof(NotBlankAttribute),
                    true
                )
                .Length > 0;

        if (hasNotBlank)
            schema.Description = string.IsNullOrEmpty(schema.Description)
                ? Note
                : $"{schema.Description} {Note}";

        return Task.CompletedTask;
    }
}
