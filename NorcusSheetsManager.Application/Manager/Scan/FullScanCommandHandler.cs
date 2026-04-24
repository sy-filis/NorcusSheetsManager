using NorcusSheetsManager.Application.Abstractions.Messaging;
using NorcusSheetsManager.Application.Abstractions.Services;
using NorcusSheetsManager.SharedKernel;

namespace NorcusSheetsManager.Application.Manager.Scan;

internal sealed class FullScanCommandHandler(IScanService scanner) : ICommandHandler<FullScanCommand>
{
  public Task<Result> Handle(FullScanCommand command, CancellationToken cancellationToken)
  {
    _ = Task.Run(scanner.FullScan, cancellationToken);
    return Task.FromResult(Result.Success());
  }
}
