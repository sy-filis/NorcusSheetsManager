using Microsoft.Extensions.Logging;
using NorcusSheetsManager.Application.Abstractions.Messaging;
using NorcusSheetsManager.Application.Abstractions.Services;
using NorcusSheetsManager.SharedKernel;

namespace NorcusSheetsManager.Application.Manager.Scan;

internal sealed class DeepScanCommandHandler(
    IScanService scanner,
    ILogger<DeepScanCommandHandler> logger) : ICommandHandler<DeepScanCommand>
{
  public Task<Result> Handle(DeepScanCommand command, CancellationToken cancellationToken)
  {
    _ = Task.Run(() =>
    {
      try
      {
        scanner.DeepScan();
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Background deep scan failed.");
      }
    }, CancellationToken.None);
    return Task.FromResult(Result.Success());
  }
}
