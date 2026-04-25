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
  private HashSet<string> _Songs { get; set; }
  private Dictionary<Guid, Transaction> _RenamingTransactions { get; }
  private IEnumerable<string> _ExtensionFilter { get; set; }
  private readonly Regex _multiPageSuffix;
  private readonly char _multiPageDelimiter;
  public string BaseSheetsFolder { get; }
  public bool HasSongs => _Songs.Count > 0;
  public IDbLoader DbLoader { get; }
  private readonly IStringDistance _stringSimilarityModel;

  public Corrector(
      IDbLoader dbLoader,
      string baseSheetsFolder,
      IEnumerable<string> extensionsFilter,
      char multiPageDelimiter,
      ILogger<Corrector> logger)
  {
    _logger = logger;
    DbLoader = dbLoader;
    BaseSheetsFolder = baseSheetsFolder;
    _Songs = [];

    _stringSimilarityModel = new QGram(2);
    _RenamingTransactions = new();
    _ExtensionFilter = extensionsFilter;
    _multiPageDelimiter = multiPageDelimiter;
    // Matches "{anything}{delimiter}{digits}" as produced by Converter for multi-page PDFs.
    _multiPageSuffix = new Regex($@"^(.+){Regex.Escape(multiPageDelimiter.ToString())}\d+$", RegexOptions.Compiled);
  }

  /// <returns>true if more than 0 songs were loaded from database</returns>
  public bool ReloadData()
  {
    DbLoader.ReloadDataAsync().Wait();
    var fresh = DbLoader.GetSongNames().ToHashSet();

    bool changed = !fresh.SetEquals(_Songs);
    _Songs = fresh;

    // When the song list changes, files that were invalid may now be valid
    // (their cached transaction is obsolete and must be dropped). The inverse —
    // files that became invalid because their matching song was renamed or
    // removed — is handled by GetRenamingTransactions on the next fetch: it
    // creates fresh transactions for any in-folder file not in _Songs and not
    // already cached, so newly-invalid files get new GUIDs naturally.
    if (changed && _RenamingTransactions.Count > 0)
    {
      var toDrop = _RenamingTransactions
          .Where(kvp =>
          {
            string nameNoExt = Path.GetFileNameWithoutExtension(kvp.Value.InvalidFullPath);
            bool wellFormed = !nameNoExt.Contains('.');
            return wellFormed && (_Songs.Contains(nameNoExt) || _IsMultiPageImage(nameNoExt));
          })
          .Select(kvp => kvp.Key)
          .ToList();
      foreach (Guid guid in toDrop)
      {
        _RenamingTransactions.Remove(guid);
      }
      if (toDrop.Count > 0)
      {
        _logger.LogInformation("Dropped {Dropped} cached transaction(s) whose files became valid after DB refresh.", toDrop.Count);
      }
    }

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

      Transaction? transaction = _RenamingTransactions.Values.FirstOrDefault(t => t.InvalidFullPath == file);

      if (transaction is null)
      {
        transaction = new Transaction(BaseSheetsFolder, file, _GetSuggestionsForFile(file, Transaction.MAX_SUGGESTIONS_COUNT), _ExtensionFilter, _multiPageDelimiter);
        _RenamingTransactions[transaction.Guid] = transaction;
      }
      transaction.SuggestionsCount = suggestionsCount;
      transactions.Add(transaction);
    }
    return transactions;
  }

  public ITransactionResponse CommitTransactionByGuid(Guid transactionGuid, int suggestionIndex)
  {
    if (!_RenamingTransactions.TryGetValue(transactionGuid, out Transaction? transaction))
    {
      return new TransactionResponse(false, $"Transaction {transactionGuid} does not exist");
    }
    ITransactionResponse response = transaction.Commit(suggestionIndex);
    _RenamingTransactions.Remove(transactionGuid);
    return response;
  }

  public ITransactionResponse CommitTransactionByGuid(Guid transactionGuid, string newFileName)
  {
    if (!_RenamingTransactions.TryGetValue(transactionGuid, out Transaction? transaction))
    {
      return new TransactionResponse(false, $"Transaction {transactionGuid} does not exist");
    }
    ITransactionResponse response = transaction.Commit(newFileName);
    _RenamingTransactions.Remove(transactionGuid);
    return response;
  }

  /// <summary>
  /// Deletes the transaction's invalid file and commits the transaction.
  /// </summary>
  public ITransactionResponse DeleteTransaction(Guid transactionGuid)
  {
    if (!_RenamingTransactions.TryGetValue(transactionGuid, out Transaction? transaction))
    {
      return new TransactionResponse(false, $"Transaction {transactionGuid} does not exist");
    }
    ITransactionResponse response = transaction.Delete();
    _RenamingTransactions.Remove(transactionGuid);
    return response;
  }

  public IRenamingTransaction? GetTransactionByGuid(Guid transactionGuid)
      => _RenamingTransactions.TryGetValue(transactionGuid, out Transaction? t) ? t : null;

  public IRenamingSuggestion CreateSuggestion(IRenamingTransaction transaction, string fileName)
      => new Suggestion(transaction.InvalidFullPath, fileName, 0, _ExtensionFilter, _multiPageDelimiter);

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
      suggestions.Add(new Suggestion(fullFileName, song, _stringSimilarityModel.Distance(name, song), _ExtensionFilter, _multiPageDelimiter));
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
