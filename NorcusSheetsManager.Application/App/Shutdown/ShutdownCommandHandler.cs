using NorcusSheetsManager.Application.Abstractions.Messaging;
using NorcusSheetsManager.Application.Abstractions.Services;
using NorcusSheetsManager.SharedKernel;

namespace NorcusSheetsManager.Application.App.Shutdown;

internal sealed class ShutdownCommandHandler(IAppLifecycle lifecycle) : ICommandHandler<ShutdownCommand>
{
  public Task<Result> Handle(ShutdownCommand command, CancellationToken cancellationToken)
  {
    lifecycle.Shutdown();
    return Task.FromResult(Result.Success());
  }
}
