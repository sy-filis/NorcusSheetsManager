using NorcusSheetsManager.Application.Abstractions.Messaging;
using NorcusSheetsManager.Application.Abstractions.Models;
using NorcusSheetsManager.Application.Abstractions.Services;
using NorcusSheetsManager.SharedKernel;

namespace NorcusSheetsManager.Application.Corrector.AutoFixInvalidNames;

internal sealed class AutoFixInvalidNamesCommandHandler(
    INameCorrector corrector,
    IAccessControl access,
    IWatcherControl watcher)
    : ICommandHandler<AutoFixInvalidNamesCommand, AutoFixInvalidNamesResponse>
{
  public Task<Result<AutoFixInvalidNamesResponse>> Handle(AutoFixInvalidNamesCommand command, CancellationToken cancellationToken)
  {
    if (!access.CanUserCommit(command.IsAdmin, command.UserId))
    {
      return Task.FromResult(Result.Failure<AutoFixInvalidNamesResponse>(CorrectorErrors.Forbidden));
    }

    if (!corrector.ReloadData())
    {
      return Task.FromResult(Result.Failure<AutoFixInvalidNamesResponse>(CorrectorErrors.NoSongsLoaded));
    }

    IEnumerable<IRenamingTransaction>? transactions = corrector.GetRenamingTransactionsForAllSubfolders(1);
    if (transactions is null)
    {
      return Task.FromResult(Result.Success(new AutoFixInvalidNamesResponse(0, 0, Array.Empty<string>())));
    }

    // Materialize so subsequent Commit calls (which mutate the corrector's internal cache) don't disrupt iteration.
    var snapshot = transactions.ToList();
    var failures = new List<string>();
    int fixedCount = 0;

    watcher.StopWatching();
    try
    {
      foreach (IRenamingTransaction trans in snapshot)
      {
        ITransactionResponse response = corrector.CommitTransactionByGuid(trans.Guid, 0);
        if (response.Success)
        {
          fixedCount++;
        }
        else
        {
          failures.Add($"{trans.InvalidFileName}: {response.Message}");
        }
      }
    }
    finally
    {
      watcher.StartWatching();
    }

    return Task.FromResult(Result.Success(new AutoFixInvalidNamesResponse(snapshot.Count, fixedCount, failures)));
  }
}
