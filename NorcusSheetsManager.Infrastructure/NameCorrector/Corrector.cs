using System.Text.RegularExpressions;
using F23.StringSimilarity;
using F23.StringSimilarity.Interfaces;
using Microsoft.Extensions.Logging;
using NorcusSheetsManager.Application.Abstractions.Models;
using NorcusSheetsManager.Application.Abstractions.Services;

namespace NorcusSheetsManager.Infrastructure.NameCorrector;

internal class Corrector : INameCorrector
{
  private readonly ILogger<Corrector> _logger;
  private List<string> _Songs { get; set; }
  private List<Transaction> _RenamingTransactions { get; set; }
  private IEnumerable<string> _ExtensionFilter { get; set; }
  private readonly Regex _multiPageSuffix;
  public string BaseSheetsFolder { get; }
  public IDbLoader DbLoader { get; }
  private readonly IStringDistance _stringSimilarityModel;

  public Corrector(
      IDbLoader dbLoader,
      string baseSheetsFolder,
      IEnumerable<string> extensionsFilter,
      string multiPageDelimiter,
      ILogger<Corrector> logger)
  {
    _logger = logger;
    DbLoader = dbLoader;
    BaseSheetsFolder = baseSheetsFolder;
    _Songs = [];

    _stringSimilarityModel = new QGram(2);
    _RenamingTransactions = new List<Transaction>();
    _ExtensionFilter = extensionsFilter;
    // Matches "{anything}{delimiter}{digits}" as produced by Converter for multi-page PDFs.
    _multiPageSuffix = new Regex($@"^(.+){Regex.Escape(multiPageDelimiter)}\d+$", RegexOptions.Compiled);
  }

  /// <returns>true if more than 0 songs were loaded from database</returns>
  public bool ReloadData()
  {
    DbLoader.ReloadDataAsync().Wait();
    _Songs = DbLoader.GetSongNames().ToList();

    if (_Songs.Count == 0)
    {
      _logger.LogWarning("No songs were loaded from database.");
      return false;
    }
    _logger.LogInformation("Database reloaded; {Count} songs loaded.", _Songs.Count);
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
        .Select(d => Path.GetFileName(d));
    foreach (string directory in directories)
    {
      result.AddRange(GetRenamingTransactions(directory, suggestionsCount) ?? Enumerable.Empty<IRenamingTransaction>());
    }
    return result;
  }

  /// <returns>Null if <paramref name="sheetsSubfolder"/> does not exist.</returns>
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
    foreach (string file in files)
    {
      string nameNoExt = Path.GetFileNameWithoutExtension(file);

      // The naming convention uses only - and _ as separators; literal dots in
      // the base name create ambiguity with extensions (Path.GetFileNameWithoutExtension
      // only strips the trailing extension), so a file like "shake.taylor.png"
      // would otherwise match a stale "shake.taylor" row in the database and
      // silently look valid. Enforce the convention here regardless of the DB.
      bool basenameIsWellFormed = !nameNoExt.Contains('.');
      if (basenameIsWellFormed && (_Songs.Contains(nameNoExt) || _IsMultiPageImage(nameNoExt)))
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
  /// Deletes the transaction's invalid file and commits the transaction.
  /// </summary>
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

  public IRenamingSuggestion CreateSuggestion(IRenamingTransaction transaction, string fileName)
      => new Suggestion(transaction.InvalidFullPath, fileName, 0);

  /// <summary>
  /// True when <paramref name="fileNameWithoutExt"/> matches the <c>{song}{delimiter}{digits}</c>
  /// pattern that <see cref="Converter"/> writes for multi-page PDFs AND the stripped prefix
  /// is the name of a song we know about. Such files are legitimately named; they must not be
  /// flagged as invalid even though their full name isn't in the song list.
  /// </summary>
  private bool _IsMultiPageImage(string fileNameWithoutExt)
  {
    Match match = _multiPageSuffix.Match(fileNameWithoutExt);
    return match.Success && _Songs.Contains(match.Groups[1].Value);
  }

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
