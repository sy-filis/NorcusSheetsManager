using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NorcusSheetsManager.Application.Abstractions.Services;

namespace NorcusSheetsManager.Infrastructure.Services;

internal sealed class AppLifecycle(IHostApplicationLifetime lifetime, ILogger<AppLifecycle> logger) : IAppLifecycle
{
  public void Shutdown()
  {
    logger.LogInformation("Shutdown requested via IAppLifecycle.Shutdown().");
    lifetime.StopApplication();
  }
}
