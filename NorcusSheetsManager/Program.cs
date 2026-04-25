using System.Diagnostics;
using System.Reflection;
using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using NorcusSheetsManager.Application;
using NorcusSheetsManager.Application.Configuration;
using NorcusSheetsManager.Infrastructure;
using NorcusSheetsManager.Infrastructure.Configuration;
using NorcusSheetsManager.SharedKernel;
using NorcusSheetsManager.Web.Api;
using NorcusSheetsManager.Web.Api.Authentication;
using NorcusSheetsManager.Web.Api.Extensions;
using Scalar.AspNetCore;

namespace NorcusSheetsManager;

internal class Program
{
  public const string ServiceName = "NorcusSheetsManager";
  public const string ServiceDisplayName = "Norcus Sheets Manager";
  public static readonly string VERSION = _GetVersion();

  private static readonly HashSet<string> _KnownHttpMethods = new(StringComparer.OrdinalIgnoreCase)
  {
    HttpMethods.Get, HttpMethods.Post, HttpMethods.Put, HttpMethods.Delete,
    HttpMethods.Patch, HttpMethods.Head, HttpMethods.Options,
    "TRACE", "CONNECT",
  };

  private static async Task<int> Main(string[] args)
  {
    if (args.Length > 0)
    {
      switch (args[0])
      {
        case "--install-service":
          return (int)_InstallService();
        case "--uninstall-service":
          return (int)_UninstallService();
        case "--help":
        case "-h":
        case "/?":
          _PrintUsage();
          return (int)ExitCode.Success;
      }
    }

    AppConfig config;
    try
    {
      config = ConfigLoader.Load();
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine($"Configuration load failed: {ex.Message}");
      return (int)ExitCode.ConfigurationError;
    }

    if (string.IsNullOrEmpty(config.Converter.SheetsPath))
    {
      Console.Error.WriteLine("Configuration error: Converter.SheetsPath is required.");
      return (int)ExitCode.ConfigurationError;
    }

    WebApplication app;
    try
    {
      app = _BuildApp(args, config);
      // StartAsync resolves the hosted services (and the Manager singleton
      // they capture) — anything throwing here is a startup-phase failure.
      await app.StartAsync();
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine($"Startup failed: {ex.Message}");
      return (int)ExitCode.StartupFailed;
    }

    try
    {
      await app.WaitForShutdownAsync();
      return (int)ExitCode.Success;
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine($"Unhandled error: {ex.Message}");
      return (int)ExitCode.GenericError;
    }
  }

  private static WebApplication _BuildApp(string[] args, AppConfig config)
  {
    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    builder.Logging.ClearProviders();
    builder.Logging.AddNLog("NLog.config");

    builder.Services.AddWindowsService(o => o.ServiceName = ServiceName);
    builder.Services.AddSystemd();

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(config);

    if (config.ApiServer.RunServer)
    {
      builder.ConfigureWebHost(config.ApiServer);
      builder.Services.AddWebApi(config.ApiServer);
      builder.Services.AddEndpoints(typeof(Web.Api.DependencyInjection).Assembly);
    }

    WebApplication app = builder.Build();
    ILogger<Program> logger = app.Services.GetRequiredService<ILogger<Program>>();

    if (config.ApiServer.RunServer)
    {
      // Force authenticator construction so the empty-key warning (and any
      // other constructor-time checks) fire at startup instead of on first
      // request that hits an auth-protected endpoint.
      _ = app.Services.GetRequiredService<ITokenAuthenticator>();

      app.Use(static async (HttpContext ctx, RequestDelegate next) =>
      {
        if (!_KnownHttpMethods.Contains(ctx.Request.Method))
        {
          ctx.Response.StatusCode = StatusCodes.Status501NotImplemented;
          return;
        }
        await next(ctx);
      });

      app.UseExceptionHandler();
      app.UseStatusCodePages();
      app.UseCors();

      ApiVersionSet versionSet = app.NewApiVersionSet()
          .HasApiVersion(new ApiVersion(1, 0))
          .ReportApiVersions()
          .Build();

      RouteGroupBuilder apiGroup = app
          .MapGroup("api/v{version:apiVersion}")
          .WithApiVersionSet(versionSet);

      app.MapEndpoints(apiGroup);

      app.MapOpenApi();
      app.MapScalarApiReference(o =>
      {
        o.Title = "Norcus Sheets Manager API";
        o.Theme = ScalarTheme.Default;
      });

      app.MapGet("/health", async (HealthCheckService healthService) =>
      {
        HealthReport report = await healthService.CheckHealthAsync();
        var payload = new
        {
          status = report.Status.ToString(),
          totalDuration = report.TotalDuration,
          entries = report.Entries.ToDictionary(
              e => e.Key,
              e => new
              {
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration,
                data = e.Value.Data,
                exception = e.Value.Exception?.Message,
              }),
        };
        int statusCode = report.Status == HealthStatus.Unhealthy
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status200OK;
        return Results.Json(payload, statusCode: statusCode);
      })
      .WithTags("Health")
      .WithSummary("Health report")
      .WithDescription("Reports the status of every registered IHealthCheck (sheets folder reachability, database connectivity). Returns 200 for Healthy or Degraded; 503 for Unhealthy.")
      .Produces(StatusCodes.Status200OK)
      .Produces(StatusCodes.Status503ServiceUnavailable);

      logger.LogInformation(
          "Norcus Sheets Manager {Version} started — API at {Url}, Scallar at {Url}/scalar, health at {Url}/health.",
          VERSION, config.ApiServer.Url, config.ApiServer.Url, config.ApiServer.Url);
    }
    else
    {
      logger.LogInformation("Norcus Sheets Manager {Version} started — file watcher only (API disabled by config).", VERSION);
    }

    return app;
  }

  private static ExitCode _InstallService()
  {
    if (!OperatingSystem.IsWindows())
    {
      Console.Error.WriteLine("Windows service installation is only supported on Windows. On Linux use systemd or Docker.");
      return ExitCode.UnsupportedPlatform;
    }

    string? exePath = Environment.ProcessPath;
    if (string.IsNullOrEmpty(exePath))
    {
      Console.Error.WriteLine("Cannot determine executable path.");
      return ExitCode.GenericError;
    }

    ExitCode result = _RunSc("create", ServiceName, "binPath=", exePath, "start=", "auto", "DisplayName=", ServiceDisplayName);
    if (result == ExitCode.Success)
    {
      Console.WriteLine($"Service '{ServiceName}' installed. Start with:  sc start {ServiceName}");
    }
    else
    {
      Console.Error.WriteLine("Service installation failed. Run from an elevated (Administrator) prompt.");
    }
    return result;
  }

  private static ExitCode _UninstallService()
  {
    if (!OperatingSystem.IsWindows())
    {
      Console.Error.WriteLine("Windows service uninstallation is only supported on Windows.");
      return ExitCode.UnsupportedPlatform;
    }

    ExitCode result = _RunSc("delete", ServiceName);
    if (result != ExitCode.Success)
    {
      Console.Error.WriteLine("Service uninstallation failed. Run from an elevated (Administrator) prompt.");
    }
    return result;
  }

  private static ExitCode _RunSc(params string[] arguments)
  {
    var startInfo = new ProcessStartInfo
    {
      FileName = "sc.exe",
      UseShellExecute = false,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
    };
    foreach (string arg in arguments)
    {
      startInfo.ArgumentList.Add(arg);
    }

    using var proc = Process.Start(startInfo);
    if (proc is null)
    {
      Console.Error.WriteLine("Failed to start sc.exe.");
      return ExitCode.ServiceManagementFailed;
    }
    proc.WaitForExit();
    Console.Write(proc.StandardOutput.ReadToEnd());
    Console.Error.Write(proc.StandardError.ReadToEnd());
    return proc.ExitCode == 0 ? ExitCode.Success : ExitCode.ServiceManagementFailed;
  }

  private static void _PrintUsage()
  {
    Console.WriteLine($$"""
        Norcus Sheets Manager {{VERSION}}

        Usage:
          NorcusSheetsManager                        Run as daemon (file watcher + REST API).
                                                     Drive via the API; Swagger UI at /swagger.
          NorcusSheetsManager --install-service      Install as Windows service (requires admin).
          NorcusSheetsManager --uninstall-service    Uninstall the Windows service (requires admin).
          NorcusSheetsManager --help                 Show this help.

        Exit codes:
          0  Success
          1  Generic / unexpected error
          2  Configuration error (missing or invalid appsettings)
          3  Unsupported platform (e.g. service install on non-Windows)
          4  Service install/uninstall failed (sc.exe error or insufficient privileges)
          5  Startup failed (DI graph or web host could not initialize)
        """);
  }

  private static string _GetVersion()
  {
    string version = Assembly.GetEntryAssembly()?.GetName()?.Version?.ToString() ?? "";
    return version.Trim();
  }
}
