using Microsoft.Extensions.Diagnostics.HealthChecks;
using NorcusSheetsManager.Application.Configuration;

namespace NorcusSheetsManager.Infrastructure.HealthChecks;

internal sealed class SheetsFolderHealthCheck(AppConfig config) : IHealthCheck
{
  public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
  {
    string? path = config.Converter.SheetsPath;
    if (string.IsNullOrEmpty(path))
    {
      return Task.FromResult(HealthCheckResult.Unhealthy("Converter.SheetsPath is not configured."));
    }
    if (!Directory.Exists(path))
    {
      return Task.FromResult(HealthCheckResult.Unhealthy(
          $"Converter.SheetsPath \"{path}\" does not exist or is not reachable."));
    }

    var data = new Dictionary<string, object> { ["path"] = path };
    return Task.FromResult(HealthCheckResult.Healthy($"Sheets folder {path} is reachable.", data));
  }
}
