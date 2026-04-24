using NorcusSheetsManager.Application.Abstractions.Messaging;
using NorcusSheetsManager.Application.Abstractions.Models;
using NorcusSheetsManager.Application.Abstractions.Services;
using NorcusSheetsManager.SharedKernel;

namespace NorcusSheetsManager.Application.Corrector.DeleteInvalidFile;

internal sealed class DeleteInvalidFileCommandHandler(INameCorrector corrector, IAccessControl access)
    : ICommandHandler<DeleteInvalidFileCommand>
{
  public Task<Result> Handle(DeleteInvalidFileCommand command, CancellationToken cancellationToken)
  {
    if (!access.CanUserCommit(command.IsAdmin, command.UserId))
    {
      return Task.FromResult(Result.Failure(CorrectorErrors.Forbidden));
    }

    ITransactionResponse response = corrector.DeleteTransaction(command.TransactionGuid);
    return Task.FromResult(response.Success
        ? Result.Success()
        : Result.Failure(Error.Problem("Corrector.Delete.Failed", response.Message ?? "Delete failed.")));
  }
}
