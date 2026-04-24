using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;
using F23.StringSimilarity;
using F23.StringSimilarity.Interfaces;

namespace NorcusSheetsManager.NameCorrector;

internal class Corrector
{
  private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
  private List<string> _Songs { get; set; }
  private List<Transaction> _RenamingTransactions { get; set; }
  private IEnumerable<string> _ExtensionFilter { get; set; }
  public string BaseSheetsFolder { get; }
  public IDbLoader DbLoader { get; private set; }
  private readonly IStringDistance _stringSimilarityModel;

  public Corrector(IDbLoader dbLoader, string baseSheetsFolder, IEnumerable<string> extensionsFilter)
  {
    DbLoader = dbLoader;
    BaseSheetsFolder = baseSheetsFolder;
    _Songs = new List<string>(dbLoader.GetSongNames());

    if (_Songs.Count == 0)
    {
      Logger.Warn("No songs were loaded from the database.", _logger);
    }
    else
    {
      Logger.Debug($"Connected to the database.", _logger);
    }

    _stringSimilarityModel = new QGram(2);
    _RenamingTransactions = new List<Transaction>();
    _ExtensionFilter = extensionsFilter;
  }
  /// <summary>
  /// 
  /// </summary>
  /// <returns>true if more than 0 songs were loaded from database</returns>
  public bool ReloadData()
  {
    DbLoader.ReloadDataAsync().Wait();
    _Songs = DbLoader.GetSongNames().ToList();

    if (_Songs.Count == 0)
    {
      Logger.Warn("No songs were loaded from database.", _logger);
      return false;
    }
    return true;
  }
  public IEnumerable<IRenamingTransaction>? GetRenamingTransactionsForAllSubfolders(int suggestionsCount)
  {
    if (!Directory.Exists(BaseSheetsFolder))
    {
      return null;
    }

    List<IRenamingTransaction> result = new();
    IEnumerable<string> directories = Directory.GetDirectories(BaseSheetsFolder)
        .Select(d => d.Replace(BaseSheetsFolder, "").Replace("\\", ""));
    foreach (string? directory in directories)
    {
      result.AddRange(GetRenamingTransactions(directory, suggestionsCount) ?? Enumerable.Empty<IRenamingTransaction>());
    }
    return result;
  }
  /// <summary>
  /// 
  /// </summary>
  /// <param name="sheetsSubfolder"></param>
  /// <param name="suggestionsCount"></param>
  /// <returns>Vrací null, pokud <paramref name="sheetsSubfolder"/> neexistuje</returns>
  public IEnumerable<IRenamingTransaction>? GetRenamingTransactions(string sheetsSubfolder, int suggestionsCount)
  {
    List<IRenamingTransaction> transactions = new();
    string path = Path.Combine(BaseSheetsFolder, sheetsSubfolder);
    if (!Directory.Exists(path))
    {
      return null;
    }

    IEnumerable<string> files = Directory.GetFiles(path, "*.*", SearchOption.TopDirectoryOnly)
        .Where(f => _ExtensionFilter.Contains(Path.GetExtension(f)));
    foreach (string? file in files)
    {
      if (_Songs.Contains(Path.GetFileNameWithoutExtension(file)))
      {
        continue;
      }

      Transaction? transaction = _RenamingTransactions.FirstOrDefault(t => t.InvalidFullPath == file);

      if (transaction is null)
      {
        transaction = new Transaction(BaseSheetsFolder, file, _GetSuggestionsForFile(file, Transaction.MAX_SUGGESTIONS_COUNT));
        _RenamingTransactions.Add(transaction);
      }
      transaction.SuggestionsCount = suggestionsCount;
      transactions.Add(transaction);
    }
    return transactions;
  }
  public ITransactionResponse CommitTransactionByGuid(Guid transactionGuid, int suggestionIndex)
  {
    Transaction? transaction = _RenamingTransactions.FirstOrDefault(t => t.Guid == transactionGuid);
    ITransactionResponse response = transaction?.Commit(suggestionIndex)
        ?? new TransactionResponse(false, $"Transaction {transactionGuid} does not exist");

    if (transaction is not null)
    {
      _RenamingTransactions.Remove(transaction);
    }

    return response;
  }
  public ITransactionResponse CommitTransactionByGuid(Guid transactionGuid, string newFileName)
  {
    Transaction? transaction = _RenamingTransactions.FirstOrDefault(t => t.Guid == transactionGuid);
    ITransactionResponse response = transaction?.Commit(newFileName)
        ?? new TransactionResponse(false, $"Transaction {transactionGuid} does not exist");

    if (transaction is not null)
    {
      _RenamingTransactions.Remove(transaction);
    }

    return response;
  }

  /// <summary>
  /// Vymaže chybný soubor transakce a transakci commituje.
  /// </summary>
  /// <param name="transactionGuid"></param>
  /// <returns></returns>
  public ITransactionResponse DeleteTransaction(Guid transactionGuid)
  {
    Transaction? transaction = _RenamingTransactions.FirstOrDefault(t => t.Guid == transactionGuid);
    ITransactionResponse response = transaction?.Delete()
        ?? new TransactionResponse(false, $"Transaction {transactionGuid} does not exist");

    if (transaction is not null)
    {
      _RenamingTransactions.Remove(transaction);
    }

    return response;
  }

  public IRenamingTransaction? GetTransactionByGuid(Guid transactionGuid)
      => _RenamingTransactions.FirstOrDefault(t => t.Guid == transactionGuid);

  private List<Suggestion> _GetSuggestionsForFile(string fullFileName, int suggestionsCount)
  {
    List<Suggestion> suggestions = new();
    string name = Path.GetFileNameWithoutExtension(fullFileName);
    foreach (string song in _Songs)
    {
      suggestions.Add(new Suggestion(fullFileName, song, _stringSimilarityModel.Distance(name, song)));
    }
    if (suggestionsCount <= 0)
    {
      suggestionsCount = 1;
    }

    if (suggestionsCount > suggestions.Count)
    {
      suggestionsCount = suggestions.Count;
    }

    if (suggestions.Count <= 1)
    {
      return suggestions;
    }

    return suggestions.OrderBy(s => s.Distance).Take(suggestionsCount).ToList();
  }
}
