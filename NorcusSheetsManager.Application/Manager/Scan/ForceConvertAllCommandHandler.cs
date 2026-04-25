using NorcusSheetsManager.Application.Abstractions.Messaging;
using NorcusSheetsManager.Application.Abstractions.Services;
using NorcusSheetsManager.SharedKernel;

namespace NorcusSheetsManager.Application.Manager.Scan;

internal sealed class ForceConvertAllCommandHandler(IScanService scanner) : ICommandHandler<ForceConvertAllCommand>
{
  public Task<Result> Handle(ForceConvertAllCommand command, CancellationToken cancellationToken)
  {
    Task.Run(scanner.ForceConvertAll, cancellationToken);
    return Task.FromResult(Result.Success());
  }
}
