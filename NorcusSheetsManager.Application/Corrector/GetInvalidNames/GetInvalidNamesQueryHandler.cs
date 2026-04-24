using NorcusSheetsManager.Application.Abstractions.Messaging;
using NorcusSheetsManager.Application.Abstractions.Models;
using NorcusSheetsManager.Application.Abstractions.Services;
using NorcusSheetsManager.SharedKernel;

namespace NorcusSheetsManager.Application.Corrector.GetInvalidNames;

internal sealed class GetInvalidNamesQueryHandler(INameCorrector corrector, IAccessControl access)
    : IQueryHandler<GetInvalidNamesQuery, InvalidNamesResponse>
{
  public Task<Result<InvalidNamesResponse>> Handle(GetInvalidNamesQuery query, CancellationToken cancellationToken)
  {
    if (!corrector.ReloadData())
    {
      return Task.FromResult(Result.Failure<InvalidNamesResponse>(CorrectorErrors.NoSongsLoaded));
    }

    string? folder = query.Folder;
    if (!access.CanUserRead(query.IsAdmin, query.UserId, ref folder))
    {
      return Task.FromResult(Result.Failure<InvalidNamesResponse>(CorrectorErrors.Forbidden));
    }

    int count = query.SuggestionsCount ?? 1;

    IEnumerable<IRenamingTransaction>? transactions = string.IsNullOrEmpty(folder)
        ? corrector.GetRenamingTransactionsForAllSubfolders(count)
        : corrector.GetRenamingTransactions(folder, count);

    if (transactions is null)
    {
      return Task.FromResult(Result.Failure<InvalidNamesResponse>(
          CorrectorErrors.FolderNotFound(folder ?? corrector.BaseSheetsFolder)));
    }

    var response = new InvalidNamesResponse(transactions, IsFolderScoped: !string.IsNullOrEmpty(query.Folder));
    return Task.FromResult(Result.Success(response));
  }
}
