using Microsoft.Extensions.Diagnostics.HealthChecks;
using NorcusSheetsManager.Application.Configuration;

namespace NorcusSheetsManager.Infrastructure.HealthChecks;

/// <summary>
/// Health check for the flat-file song-list backend (DbFileLoader). Reports Healthy
/// when the file exists, Unhealthy otherwise. Used in place of AddMySql when the
/// configured DbConnection.Database points at a .txt file.
/// </summary>
internal sealed class DbFileHealthCheck(AppConfig config) : IHealthCheck
{
  public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
  {
    string path = config.DbConnection.Database;
    var data = new Dictionary<string, object>
    {
      ["path"] = path,
    };

    return Task.FromResult(
      !File.Exists(path) ?
        HealthCheckResult.Unhealthy($"Song file '{path}' does not exist.", data: data) :
        HealthCheckResult.Healthy($"Song file '{path}' is reachable.", data)
    );
  }
}
