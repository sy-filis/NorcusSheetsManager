using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NorcusSheetsManager.Application.Abstractions.Services;
using NorcusSheetsManager.Application.Configuration;

namespace NorcusSheetsManager.Infrastructure.NameCorrector;

internal sealed class DbRefreshHostedService(
    INameCorrector corrector,
    AppConfig config,
    ILogger<DbRefreshHostedService> logger) : BackgroundService
{
  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    _ReloadOnce(initial: true);

    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(config.DbConnection.RefreshIntervalSeconds));
    try
    {
      while (await timer.WaitForNextTickAsync(stoppingToken))
      {
        _ReloadOnce(initial: false);
      }
    }
    catch (OperationCanceledException)
    {
    }
  }

  private void _ReloadOnce(bool initial)
  {
    try
    {
      corrector.ReloadData();
    }
    catch (Exception ex)
    {
      if (initial)
      {
        logger.LogError(ex, "Initial database load failed. App will continue; song list is empty until the next successful refresh.");
      }
      else
      {
        logger.LogWarning(ex, "Database refresh failed; keeping previously loaded state.");
      }
    }
  }
}
