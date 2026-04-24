using NorcusSheetsManager.Application.Abstractions.Messaging;
using NorcusSheetsManager.Application.Abstractions.Models;
using NorcusSheetsManager.Application.Abstractions.Services;
using NorcusSheetsManager.SharedKernel;

namespace NorcusSheetsManager.Application.Corrector.CheckFileExists;

internal sealed class CheckFileExistsQueryHandler(INameCorrector corrector)
    : IQueryHandler<CheckFileExistsQuery, IRenamingSuggestion>
{
  public Task<Result<IRenamingSuggestion>> Handle(CheckFileExistsQuery query, CancellationToken cancellationToken)
  {
    IRenamingTransaction? transaction = corrector.GetTransactionByGuid(query.TransactionGuid);
    if (transaction is null)
    {
      return Task.FromResult(Result.Failure<IRenamingSuggestion>(CorrectorErrors.TransactionNotFound(query.TransactionGuid)));
    }

    IRenamingSuggestion suggestion = corrector.CreateSuggestion(transaction, query.FileName);
    return Task.FromResult(Result.Success(suggestion));
  }
}
