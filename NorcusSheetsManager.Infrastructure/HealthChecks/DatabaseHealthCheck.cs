using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using NorcusSheetsManager.Application.Configuration;
using NorcusSheetsManager.Infrastructure.NameCorrector;

namespace NorcusSheetsManager.Infrastructure.HealthChecks;

/// <summary>
/// Lightweight liveness check for the song/user store. Probes whether the loader can
/// still reach its backing store — for MySqlConnector-backed loaders that means a
/// successful connection + query round-trip; for the .txt file loader it means the
/// file is readable.
/// </summary>
internal sealed class DatabaseHealthCheck(
    AppConfig config,
    IDbLoader loader,
    ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
  private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

  public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
  {
    DatabaseConnection db = config.DbConnection;
    var data = new Dictionary<string, object>
    {
      ["server"] = db.Server,
      ["database"] = db.Database,
    };

    try
    {
      await loader.ReloadDataAsync().WaitAsync(ProbeTimeout, cancellationToken);

      int songCount = loader.GetSongNames().Count();
      data["songs"] = songCount;

      return songCount > 0
          ? HealthCheckResult.Healthy($"Database reachable; {songCount} song(s) loaded.", data)
          : HealthCheckResult.Degraded("Database reachable but no songs were returned.", data: data);
    }
    catch (TimeoutException ex)
    {
      logger.LogWarning(ex, "Database health probe timed out after {Timeout}.", ProbeTimeout);
      return HealthCheckResult.Unhealthy($"Database probe timed out after {ProbeTimeout}.", ex, data);
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Database health probe threw an exception.");
      return HealthCheckResult.Unhealthy("Database probe failed.", ex, data);
    }
  }
}
