using Asp.Versioning;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using NorcusSheetsManager.Application.Configuration;
using NorcusSheetsManager.Web.Api.Authentication;
using NorcusSheetsManager.Web.Api.Infrastructure;

namespace NorcusSheetsManager.Web.Api;

public static class DependencyInjection
{
  public static IServiceCollection AddWebApi(this IServiceCollection services, ApiServerSettings settings)
  {
    services.AddSingleton<ITokenAuthenticator>(sp => new JWTAuthenticator(
        settings.Key,
        sp.GetRequiredService<ILogger<JWTAuthenticator>>()));

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

    services.AddEndpointsApiExplorer();
    services.AddSwaggerGen(c =>
    {
      c.SwaggerDoc("v1", new OpenApiInfo
      {
        Title = "Norcus Sheets Manager API",
        Version = "v1",
        Description = "PDF→image sync, file-name corrector and admin endpoints for the Norcus sheet-music library."
      });

      var bearerScheme = new OpenApiSecurityScheme
      {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the JWT (without the \"Bearer \" prefix).",
        Reference = new OpenApiReference
        {
          Type = ReferenceType.SecurityScheme,
          Id = "Bearer",
        }
      };
      c.AddSecurityDefinition("Bearer", bearerScheme);
      c.AddSecurityRequirement(new OpenApiSecurityRequirement
      {
        [bearerScheme] = Array.Empty<string>()
      });
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
