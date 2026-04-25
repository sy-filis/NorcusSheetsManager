using Microsoft.Extensions.Logging;
using NorcusSheetsManager.Application.Abstractions.Messaging;
using NorcusSheetsManager.Application.Abstractions.Services;
using NorcusSheetsManager.SharedKernel;

namespace NorcusSheetsManager.Application.Manager.Scan;

internal sealed class ForceConvertAllCommandHandler(
    IScanService scanner,
    ILogger<ForceConvertAllCommandHandler> logger) : ICommandHandler<ForceConvertAllCommand>
{
  public Task<Result> Handle(ForceConvertAllCommand command, CancellationToken cancellationToken)
  {
    _ = Task.Run(() =>
    {
      try
      {
        scanner.ForceConvertAll();
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Background force-convert-all failed.");
      }
    }, CancellationToken.None);
    return Task.FromResult(Result.Success());
  }
}
