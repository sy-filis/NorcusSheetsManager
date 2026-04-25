using NorcusSheetsManager.Application.Abstractions.Models;

namespace NorcusSheetsManager.Application.Abstractions.Services;

public interface INameCorrector
{
  string BaseSheetsFolder { get; }
  bool HasSongs { get; }
  bool ReloadData();
  IEnumerable<IRenamingTransaction>? GetRenamingTransactionsForAllSubfolders(int suggestionsCount);
  IEnumerable<IRenamingTransaction>? GetRenamingTransactions(string sheetsSubfolder, int suggestionsCount);
  ITransactionResponse CommitTransactionByGuid(Guid transactionGuid, int suggestionIndex);
  ITransactionResponse CommitTransactionByGuid(Guid transactionGuid, string newFileName);
  ITransactionResponse DeleteTransaction(Guid transactionGuid);
  IRenamingTransaction? GetTransactionByGuid(Guid transactionGuid);
  IRenamingSuggestion CreateSuggestion(IRenamingTransaction transaction, string fileName);
}
