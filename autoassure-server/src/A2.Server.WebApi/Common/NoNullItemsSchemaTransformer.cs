using A2.Server.Common;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace A2.Server.WebApi.Common;

/// <summary>
/// Appends a note to a Contract property's OpenAPI schema description when it
/// carries <see cref="NoNullItemsAttribute" />, since that constraint isn't
/// otherwise expressible as a JSON Schema keyword and would go undocumented in
/// the generated spec/SDK.
/// </summary>
public class NoNullItemsSchemaTransformer : IOpenApiSchemaTransformer
{
    private const string Note = "Items must not be null.";

    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        var hasNoNullItems =
            context
                .JsonPropertyInfo?.AttributeProvider?.GetCustomAttributes(
                    typeof(NoNullItemsAttribute),
                    true
                )
                .Length > 0;

        if (hasNoNullItems)
            schema.Description = string.IsNullOrEmpty(schema.Description)
                ? Note
                : $"{schema.Description} {Note}";

        return Task.CompletedTask;
    }
}
