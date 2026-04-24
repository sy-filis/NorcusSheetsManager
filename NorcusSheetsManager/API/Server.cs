using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using NorcusSheetsManager.API.Resources;
using NorcusSheetsManager.NameCorrector;

namespace NorcusSheetsManager.API;

public static class Server
{
  private static WebApplication? _app;

  public static void Initialize(int port, string secureKey, List<(Type type, object instance)> singletons)
  {
    if (_app is not null)
    {
      throw new Exception("Instance is already created.");
    }

    WebApplicationBuilder builder = WebApplication.CreateBuilder();

    builder.Logging.ClearProviders();
    builder.Logging.AddNLog("NLog.config");

    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

    builder.Services.AddSingleton<ITokenAuthenticator>(new JWTAuthenticator(secureKey));
    foreach ((Type? type, object? instance) in singletons)
    {
      builder.Services.AddSingleton(type, instance);
    }

    builder.Services.AddCors(options =>
    {
      options.AddDefaultPolicy(policy => policy
                .AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader()
                .SetPreflightMaxAge(TimeSpan.FromSeconds(86400)));
    });

    _app = builder.Build();
    _app.UseCors();

    MasterResource.MapEndpoints(_app);
    NameCorrectorResource.MapEndpoints(_app);
    ManagerResource.MapEndpoints(_app);
  }

  public static void Start()
  {
    if (_app is null)
    {
      throw new Exception("Server is not initialized. Call " + nameof(Initialize));
    }

    _app.StartAsync().GetAwaiter().GetResult();
  }

  public static void Stop()
  {
    _app?.StopAsync().GetAwaiter().GetResult();
  }
}
