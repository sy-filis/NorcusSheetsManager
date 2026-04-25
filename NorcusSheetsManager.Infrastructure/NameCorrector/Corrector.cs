using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using F23.StringSimilarity;
using F23.StringSimilarity.Interfaces;
using Microsoft.Extensions.Logging;
using NorcusSheetsManager.Application.Abstractions.Models;
using NorcusSheetsManager.Application.Abstractions.Services;

namespace NorcusSheetsManager.Infrastructure.NameCorrector;

internal class Corrector(
  IDbLoader dbLoader,
  string baseSheetsFolder,
  IEnumerable<string> extensionsFilter,
  char multiPageDelimiter,
  ILogger<Corrector> logger)
  : INameCorrector
{
  private HashSet<string> Songs { get; set; } = [];
  private Dictionary<Guid, Transaction> RenamingTransactions { get; } = new();
  private IEnumerable<string> ExtensionFilter { get; init; } = extensionsFilter;
  private readonly Regex _multiPageSuffix = new($@"^(.+){Regex.Escape(multiPageDelimiter.ToString())}\d+$", RegexOptions.Compiled);
  private readonly ConcurrentDictionary<string, byte> _invalidPaths = new(StringComparer.OrdinalIgnoreCase);
  private readonly Lock _indexLock = new();
  public string BaseSheetsFolder { get; } = baseSheetsFolder;
  public bool HasSongs => Songs.Count > 0;
  public IDbLoader DbLoader { get; } = dbLoader;
  private readonly IStringDistance _stringSimilarityModel = new QGram(2);

  // Matches "{anything}{delimiter}{digits}" as produced by Converter for multi-page PDFs.

  /// <returns>true if more than 0 songs were loaded from database</returns>
  public bool ReloadData()
  {
    DbLoader.ReloadDataAsync().Wait();
    var fresh = DbLoader.GetSongNames().ToHashSet();

    bool changed = !fresh.SetEquals(Songs);
    Songs = fresh;

    // The validity of every cached path depends on _Songs, so a song-list change
    // invalidates the index. RebuildInvalidIndex re-scans disk and drops cached
    // transactions whose files are no longer in the invalid set (became valid,
    // were deleted, or were renamed away).
    if (changed)
    {
      RebuildInvalidIndex();
    }

    if (Songs.Count == 0)
    {
      logger.LogWarning("No songs were loaded from database.");
      return false;
    }
    logger.LogInformation("Database reloaded; {Count} songs loaded.", Songs.Count);
    return true;
  }

  public void RebuildInvalidIndex()
  {
    if (Songs.Count == 0 || !Directory.Exists(BaseSheetsFolder))
    {
      // Without a song list, every file would look "invalid". Skip until ReloadData
      // succeeds — at which point ReloadData itself triggers the rebuild.
      return;
    }

    var fresh = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (string dir in Directory.GetDirectories(BaseSheetsFolder))
    {
      string[] files;
      try
      {
        files = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly);
      }
      catch (DirectoryNotFoundException)
      {
        continue;
      }
      foreach (string file in files)
      {
        if (_IsInvalid(file))
        {
          fresh.Add(file);
        }
      }
    }

    lock (_indexLock)
    {
      _invalidPaths.Clear();
      foreach (string path in fresh)
      {
        _invalidPaths.TryAdd(path, 0);
      }

      // Drop cached transactions whose file is no longer flagged invalid
      // (deleted, renamed away, or now matches a song under the new song list).
      var stale = RenamingTransactions
          .Where(kvp => !_invalidPaths.ContainsKey(kvp.Value.InvalidFullPath))
          .Select(kvp => kvp.Key)
          .ToList();
      foreach (Guid g in stale)
      {
        RenamingTransactions.Remove(g);
      }
      if (stale.Count > 0)
      {
        logger.LogInformation("Dropped {Dropped} cached transaction(s) no longer in the invalid set.", stale.Count);
      }
    }

    logger.LogInformation("Invalid-files index rebuilt; {Count} file(s) currently invalid.", _invalidPaths.Count);
  }

  public void OnFileCreated(string fullPath)
  {
    if (_IsInvalid(fullPath))
    {
      _invalidPaths.TryAdd(fullPath, 0);
    }
  }

  public void OnFileRenamed(string oldFullPath, string newFullPath)
  {
    _invalidPaths.TryRemove(oldFullPath, out _);
    _DropCachedTransactionFor(oldFullPath);
    if (_IsInvalid(newFullPath))
    {
      _invalidPaths.TryAdd(newFullPath, 0);
    }
  }

  public void OnFileDeleted(string fullPath)
  {
    _invalidPaths.TryRemove(fullPath, out _);
    _DropCachedTransactionFor(fullPath);
  }

  public int GetInvalidCount(string? sheetsSubfolder = null)
  {
    if (string.IsNullOrEmpty(sheetsSubfolder))
    {
      return _invalidPaths.Count;
    }
    string prefix = Path.Combine(BaseSheetsFolder, sheetsSubfolder).TrimEnd(Path.DirectorySeparatorChar)
        + Path.DirectorySeparatorChar;
    int count = 0;
    foreach (string path in _invalidPaths.Keys)
    {
      if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
      {
        count++;
      }
    }
    return count;
  }

  private bool _IsInvalid(string fullPath)
  {
    string ext = Path.GetExtension(fullPath);
    if (!ExtensionFilter.Contains(ext))
    {
      return false;
    }
    string nameNoExt = Path.GetFileNameWithoutExtension(fullPath);
    bool wellFormed = !nameNoExt.Contains('.');
    if (wellFormed && (Songs.Contains(nameNoExt) || _IsMultiPageImage(nameNoExt)))
    {
      return false;
    }
    return true;
  }

  private void _DropCachedTransactionFor(string fullPath)
  {
    Guid staleGuid = RenamingTransactions
        .Where(kvp => string.Equals(kvp.Value.InvalidFullPath, fullPath, StringComparison.OrdinalIgnoreCase))
        .Select(kvp => kvp.Key)
        .FirstOrDefault();
    if (staleGuid != Guid.Empty)
    {
      RenamingTransactions.Remove(staleGuid);
    }
  }

  public IEnumerable<IRenamingTransaction>? GetRenamingTransactionsForAllSubfolders(int suggestionsCount)
  {
    if (!Directory.Exists(BaseSheetsFolder))
    {
      return null;
    }
    string prefix = BaseSheetsFolder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    return _BuildTransactionsForPrefix(prefix, suggestionsCount);
  }

  /// <returns>Null if <paramref name="sheetsSubfolder"/> does not exist.</returns>
  public IEnumerable<IRenamingTransaction>? GetRenamingTransactions(string sheetsSubfolder, int suggestionsCount)
  {
    string path = Path.Combine(BaseSheetsFolder, sheetsSubfolder);
    if (!Directory.Exists(path))
    {
      return null;
    }
    string prefix = path.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    return _BuildTransactionsForPrefix(prefix, suggestionsCount);
  }

  /// <summary>
  /// Walks the in-memory invalid-files index (no filesystem hit), filters by path
  /// prefix, and reuses or builds the matching <see cref="Transaction"/>. Suggestion
  /// ranking is paid only when a Transaction is first materialized for a given path.
  /// </summary>
  private List<IRenamingTransaction> _BuildTransactionsForPrefix(string prefix, int suggestionsCount)
  {
    List<IRenamingTransaction> transactions = new();
    foreach (string file in _invalidPaths.Keys)
    {
      if (!file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }
      Transaction? transaction = RenamingTransactions.Values.FirstOrDefault(t => t.InvalidFullPath == file);
      if (transaction is null)
      {
        transaction = new Transaction(BaseSheetsFolder, file, _GetSuggestionsForFile(file, Transaction.MAX_SUGGESTIONS_COUNT), ExtensionFilter, multiPageDelimiter);
        RenamingTransactions[transaction.Guid] = transaction;
      }
      transaction.SuggestionsCount = suggestionsCount;
      transactions.Add(transaction);
    }
    return transactions;
  }

  public ITransactionResponse CommitTransactionByGuid(Guid transactionGuid, int suggestionIndex)
  {
    if (!RenamingTransactions.TryGetValue(transactionGuid, out Transaction? transaction))
    {
      return new TransactionResponse(false, $"Transaction {transactionGuid} does not exist");
    }
    ITransactionResponse response = transaction.Commit(suggestionIndex);
    RenamingTransactions.Remove(transactionGuid);
    return response;
  }

  public ITransactionResponse CommitTransactionByGuid(Guid transactionGuid, string newFileName)
  {
    if (!RenamingTransactions.TryGetValue(transactionGuid, out Transaction? transaction))
    {
      return new TransactionResponse(false, $"Transaction {transactionGuid} does not exist");
    }
    ITransactionResponse response = transaction.Commit(newFileName);
    RenamingTransactions.Remove(transactionGuid);
    return response;
  }

  /// <summary>
  /// Deletes the transaction's invalid file and commits the transaction.
  /// </summary>
  public ITransactionResponse DeleteTransaction(Guid transactionGuid)
  {
    if (!RenamingTransactions.TryGetValue(transactionGuid, out Transaction? transaction))
    {
      return new TransactionResponse(false, $"Transaction {transactionGuid} does not exist");
    }
    ITransactionResponse response = transaction.Delete();
    RenamingTransactions.Remove(transactionGuid);
    return response;
  }

  public IRenamingTransaction? GetTransactionByGuid(Guid transactionGuid)
      => RenamingTransactions.TryGetValue(transactionGuid, out Transaction? t) ? t : null;

  public IRenamingSuggestion CreateSuggestion(IRenamingTransaction transaction, string fileName)
      => new Suggestion(transaction.InvalidFullPath, fileName, 0, ExtensionFilter, multiPageDelimiter);

  /// <summary>
  /// True when <paramref name="fileNameWithoutExt"/> matches the <c>{song}{delimiter}{digits}</c>
  /// pattern that <see cref="Converter"/> writes for multi-page PDFs AND the stripped prefix
  /// is the name of a song we know about. Such files are legitimately named; they must not be
  /// flagged as invalid even though their full name isn't in the song list.
  /// </summary>
  private bool _IsMultiPageImage(string fileNameWithoutExt)
  {
    Match match = _multiPageSuffix.Match(fileNameWithoutExt);
    return match.Success && Songs.Contains(match.Groups[1].Value);
  }

  private List<Suggestion> _GetSuggestionsForFile(string fullFileName, int suggestionsCount)
  {
    List<Suggestion> suggestions = new();
    string name = Path.GetFileNameWithoutExtension(fullFileName);
    foreach (string song in Songs)
    {
      suggestions.Add(new Suggestion(fullFileName, song, _stringSimilarityModel.Distance(name, song), ExtensionFilter, multiPageDelimiter));
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
