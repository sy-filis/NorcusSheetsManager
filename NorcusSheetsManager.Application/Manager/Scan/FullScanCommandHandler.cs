using Microsoft.Extensions.Logging;
using NorcusSheetsManager.Application.Abstractions.Messaging;
using NorcusSheetsManager.Application.Abstractions.Services;
using NorcusSheetsManager.SharedKernel;

namespace NorcusSheetsManager.Application.Manager.Scan;

internal sealed class FullScanCommandHandler(
    IScanService scanner,
    ILogger<FullScanCommandHandler> logger) : ICommandHandler<FullScanCommand>
{
  public Task<Result> Handle(FullScanCommand command, CancellationToken cancellationToken)
  {
    // Fire-and-forget: the request returns immediately and the scan runs in the
    // background. We don't pass `cancellationToken` because it's the request's
    // token — by the time the work runs, the request is already over and the
    // token is dead. The catch is what keeps an exception from disappearing
    // into TaskScheduler.UnobservedTaskException.
    _ = Task.Run(() =>
    {
      try
      {
        scanner.FullScan();
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Background full scan failed.");
      }
    }, CancellationToken.None);
    return Task.FromResult(Result.Success());
  }
}
