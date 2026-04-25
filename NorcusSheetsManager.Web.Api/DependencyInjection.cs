using Asp.Versioning;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NorcusSheetsManager.Application.Configuration;
using NorcusSheetsManager.Web.Api.Authentication;
using NorcusSheetsManager.Web.Api.Infrastructure;

namespace NorcusSheetsManager.Web.Api;

public static class DependencyInjection
{
  public static IServiceCollection AddWebApi(this IServiceCollection services, ApiServerSettings settings)
  {
    services.AddSingleton<ITokenAuthenticator>(sp => new JWTAuthenticator(
      settings.JwtSigningKey,
      sp.GetRequiredService<ILogger<JWTAuthenticator>>(),
      sp.GetRequiredService<IHostEnvironment>())
    );

    services.AddCors(options =>
    {
      options.AddDefaultPolicy(policy => policy
        .AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader()
        .SetPreflightMaxAge(TimeSpan.FromSeconds(86400)));
    });

    services.AddExceptionHandler<GlobalExceptionHandler>();
    services.AddProblemDetails();

    services.AddApiVersioning(o =>
    {
      o.DefaultApiVersion = new ApiVersion(1, 0);
      o.AssumeDefaultVersionWhenUnspecified = true;
      o.ReportApiVersions = true;
      o.ApiVersionReader = new UrlSegmentApiVersionReader();
    })
    .AddApiExplorer(o =>
    {
      o.GroupNameFormat = "'v'VVV";
      o.SubstituteApiVersionInUrl = true;
    });

    services.AddOpenApi(o =>
    {
      o.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
      o.AddOperationTransformer<ResponseExamplesTransformer>();
    });

    return services;
  }

  public static WebApplicationBuilder ConfigureWebHost(this WebApplicationBuilder builder, ApiServerSettings settings)
  {
    builder.WebHost.UseUrls(settings.Url);
    builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);
    return builder;
  }
}
