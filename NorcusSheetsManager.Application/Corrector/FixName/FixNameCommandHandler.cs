using System.Text;
using NorcusSheetsManager.Application.Abstractions.Messaging;
using NorcusSheetsManager.Application.Abstractions.Models;
using NorcusSheetsManager.Application.Abstractions.Services;
using NorcusSheetsManager.SharedKernel;

namespace NorcusSheetsManager.Application.Corrector.FixName;

internal sealed class FixNameCommandHandler(INameCorrector corrector, IAccessControl access)
    : ICommandHandler<FixNameCommand>
{
  public Task<Result> Handle(FixNameCommand command, CancellationToken cancellationToken)
  {
    if (!access.CanUserCommit(command.IsAdmin, command.UserId))
    {
      return Task.FromResult(Result.Failure(CorrectorErrors.Forbidden));
    }

    if (string.IsNullOrEmpty(command.FileName) && !command.SuggestionIndex.HasValue)
    {
      var msg = new StringBuilder("Both \"FileName\" and \"SuggestionIndex\" values are null. One of them must be set.");
      if (command.TransactionGuid == Guid.Empty)
      {
        msg.Append(" Parameter \"TransactionGuid\" is invalid");
      }
      return Task.FromResult(Result.Failure(Error.Problem("Corrector.FixName.Invalid", msg.ToString())));
    }

    ITransactionResponse response = command.SuggestionIndex.HasValue
        ? corrector.CommitTransactionByGuid(command.TransactionGuid, command.SuggestionIndex.Value)
        : corrector.CommitTransactionByGuid(command.TransactionGuid, command.FileName!);

    return Task.FromResult(response.Success
        ? Result.Success()
        : Result.Failure(Error.Problem("Corrector.FixName.Failed", response.Message ?? "Commit failed.")));
  }
}
