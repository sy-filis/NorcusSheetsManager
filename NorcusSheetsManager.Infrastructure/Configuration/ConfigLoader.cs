using Microsoft.Extensions.Configuration;
using NorcusSheetsManager.Application.Configuration;

namespace NorcusSheetsManager.Infrastructure.Configuration;

public static class ConfigLoader
{
  private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

  public static AppConfig Load()
  {
    string env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
              ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
              ?? "Production";

    _logger.Info("Loading configuration (environment: {0}).", env);

    IConfigurationRoot configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
        .AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: false)
        .AddEnvironmentVariables()
        .Build();

    return configuration.Get<AppConfig>() ?? new AppConfig();
  }
}
