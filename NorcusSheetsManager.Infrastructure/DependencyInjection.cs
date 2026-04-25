using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;
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

    DatabaseConnection dbConfig = config.DbConnection;
    bool isFileBackend = File.Exists(dbConfig.Database) && Path.GetExtension(dbConfig.Database) == ".txt";

    if (isFileBackend)
    {
      services.AddSingleton<IDbLoader>(_ => new DbFileLoader(dbConfig.Database) { UserId = dbConfig.UserId });
    }
    else
    {
      services.AddSingleton<IDbLoader>(_ => new MySQLLoader(
          dbConfig.Server, dbConfig.Port, dbConfig.Database, dbConfig.UserId, dbConfig.Password));
    }

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

    IHealthChecksBuilder healthChecks = services.AddHealthChecks()
        .AddCheck<SheetsFolderHealthCheck>("sheets-folder", tags: ["ready"]);

    if (isFileBackend)
    {
      healthChecks.AddCheck<DbFileHealthCheck>("database", tags: ["ready"]);
    }
    else
    {
      string mysqlConnectionString = new MySqlConnectionStringBuilder
      {
        Server = dbConfig.Server,
        Port = dbConfig.Port,
        Database = dbConfig.Database,
        UserID = dbConfig.UserId,
        Password = dbConfig.Password,
      }.ConnectionString;

      healthChecks.AddMySql(mysqlConnectionString, name: "database", tags: ["ready"]);
    }

    return services;
  }
}
