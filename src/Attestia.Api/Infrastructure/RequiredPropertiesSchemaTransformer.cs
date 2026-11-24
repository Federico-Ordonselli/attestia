using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Attestia.Api.Infrastructure;

/// <summary>
/// Nel documento OpenAPI generato da .NET le proprieta' non annullabili dei record non risultano "required":
/// i client generati le vedrebbero tutte opzionali (<c>id?: number</c>). Qui ogni proprieta' il cui schema
/// non ammette null viene dichiarata obbligatoria, cosi' il contratto riflette i tipi C#.
/// </summary>
internal sealed class RequiredPropertiesSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (schema.Properties is not { Count: > 0 })
        {
            return Task.CompletedTask;
        }

        // Si decide dal tipo .NET, non dallo schema: il wrapper oneOf [null, $ref] degli enum annullabili
        // viene aggiunto dopo i transformer.
        schema.Required ??= new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in context.JsonTypeInfo.Properties)
        {
            var nullable = Nullable.GetUnderlyingType(property.PropertyType) is not null || property.IsGetNullable
                || (schema.Properties.TryGetValue(property.Name, out var p) && p is OpenApiSchema { Type: { } t } && t.HasFlag(JsonSchemaType.Null));
            if (!nullable && schema.Properties.ContainsKey(property.Name))
            {
                schema.Required.Add(property.Name);
            }
        }

        return Task.CompletedTask;
    }
}
