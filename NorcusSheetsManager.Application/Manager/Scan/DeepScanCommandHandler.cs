using NorcusSheetsManager.Application.Abstractions.Messaging;
using NorcusSheetsManager.Application.Abstractions.Services;
using NorcusSheetsManager.SharedKernel;

namespace NorcusSheetsManager.Application.Manager.Scan;

internal sealed class DeepScanCommandHandler(IScanService scanner) : ICommandHandler<DeepScanCommand>
{
  public Task<Result> Handle(DeepScanCommand command, CancellationToken cancellationToken)
  {
    _ = Task.Run(scanner.DeepScan, cancellationToken);
    return Task.FromResult(Result.Success());
  }
}
