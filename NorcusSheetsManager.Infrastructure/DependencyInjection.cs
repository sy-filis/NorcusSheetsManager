using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NorcusSheetsManager.Application.Abstractions.Services;
using NorcusSheetsManager.Application.Configuration;
using NorcusSheetsManager.Infrastructure.HealthChecks;
using NorcusSheetsManager.Infrastructure.Manager;
using NorcusSheetsManager.Infrastructure.NameCorrector;
using NorcusSheetsManager.Infrastructure.Services;

namespace NorcusSheetsManager.Infrastructure;

public static class DependencyInjection
{
  public static IServiceCollection AddInfrastructure(this IServiceCollection services, AppConfig config)
  {
    services.AddSingleton(config);

    services.AddSingleton<IDbLoader>(_ =>
    {
      DatabaseConnection db = config.DbConnection;
      if (File.Exists(db.Database) && Path.GetExtension(db.Database) == ".txt")
      {
        return new DbFileLoader(db.Database) { UserId = db.UserId };
      }
      return new MySQLLoader(
          db.Server, db.Port, db.Database, db.UserId, db.Password);
    });

    services.AddSingleton<INameCorrector>(sp => new Corrector(
        sp.GetRequiredService<IDbLoader>(),
        config.Converter.SheetsPath!,
        config.Converter.WatchedExtensions,
        config.Converter.MultiPageDelimiter,
        sp.GetRequiredService<ILogger<Corrector>>()));

    services.AddSingleton<IFileNameNormalizer, Manager.FileNameNormalizer>();
    services.AddSingleton<Converter>();
    services.AddSingleton<Manager.Manager>();
    services.AddSingleton<IScanService>(sp => sp.GetRequiredService<Manager.Manager>());
    services.AddSingleton<IWatcherControl>(sp => sp.GetRequiredService<Manager.Manager>());
    services.AddSingleton<IFolderBrowser, FolderBrowser>();
    services.AddSingleton<IAccessControl, AccessControl>();
    services.AddSingleton<IAppLifecycle, AppLifecycle>();

    services.AddHostedService<ManagerHostedService>();
    services.AddHostedService<DbRefreshHostedService>();

    services.AddHealthChecks()
        .AddCheck<SheetsFolderHealthCheck>("sheets-folder", tags: ["ready"])
        .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

    return services;
  }
}
