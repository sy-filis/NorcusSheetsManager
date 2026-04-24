using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NorcusSheetsManager.Infrastructure.Manager;

internal sealed class ManagerHostedService(Manager manager, ILogger<ManagerHostedService> logger) : IHostedService
{
  public Task StartAsync(CancellationToken cancellationToken)
  {
    manager.FullScan();
    manager.StartWatching(true);
    if (manager.Config.Converter.AutoScan)
    {
      manager.AutoFullScan(60000, 5);
    }
    logger.LogInformation("Norcus Sheets Manager started (daemon mode).");
    return Task.CompletedTask;
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    manager.StopWatching();
    logger.LogInformation("Norcus Sheets Manager stopped.");
    return Task.CompletedTask;
  }
}
