using NorcusSheetsManager.Application.Abstractions.Messaging;
using NorcusSheetsManager.Application.Abstractions.Models;
using NorcusSheetsManager.Application.Abstractions.Services;
using NorcusSheetsManager.SharedKernel;

namespace NorcusSheetsManager.Application.Corrector.GetInvalidNamesCount;

internal sealed class GetInvalidNamesCountQueryHandler(INameCorrector corrector, IAccessControl access)
    : IQueryHandler<GetInvalidNamesCountQuery, int>
{
  public Task<Result<int>> Handle(GetInvalidNamesCountQuery query, CancellationToken cancellationToken)
  {
    if (!corrector.ReloadData())
    {
      return Task.FromResult(Result.Failure<int>(CorrectorErrors.NoSongsLoaded));
    }

    string? folder = query.Folder;
    if (!access.CanUserRead(query.IsAdmin, query.UserId, ref folder))
    {
      return Task.FromResult(Result.Failure<int>(CorrectorErrors.Forbidden));
    }

    IEnumerable<IRenamingTransaction>? transactions = string.IsNullOrEmpty(folder)
        ? corrector.GetRenamingTransactionsForAllSubfolders(1)
        : corrector.GetRenamingTransactions(folder, 1);

    if (transactions is null)
    {
      return Task.FromResult(Result.Failure<int>(
          CorrectorErrors.FolderNotFound(folder ?? corrector.BaseSheetsFolder)));
    }

    return Task.FromResult(Result.Success(transactions.Count()));
  }
}
