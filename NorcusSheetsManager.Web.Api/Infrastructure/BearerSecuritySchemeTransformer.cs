using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace NorcusSheetsManager.Web.Api.Infrastructure;

/// <summary>
/// Document transformer that registers a JWT Bearer security scheme on the generated
/// OpenAPI document and applies it as the default requirement. Without it, Scalar /
/// Swagger UI doesn't show an "Authorize" button.
/// </summary>
public sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
  public Task TransformAsync(
      OpenApiDocument document,
      OpenApiDocumentTransformerContext context,
      CancellationToken cancellationToken)
  {
    document.Components ??= new OpenApiComponents();
    document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
    document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
    {
      Type = SecuritySchemeType.Http,
      Scheme = "bearer",
      BearerFormat = "JWT",
      In = ParameterLocation.Header,
      Description = "Paste the JWT (without the \"Bearer \" prefix).",
    };

    var requirement = new OpenApiSecurityRequirement
    {
      [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>(),
    };
    document.Security ??= [];
    document.Security.Add(requirement);

    return Task.CompletedTask;
  }
}
