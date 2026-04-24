using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NorcusSheetsManager.NameCorrector;

internal class Transaction(string baseFolder, string invalidFullName, IEnumerable<IRenamingSuggestion> suggestions) : IRenamingTransaction
{
  public const int MAX_SUGGESTIONS_COUNT = 10;
  private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
  public Guid Guid { get; } = Guid.NewGuid();
  public string InvalidFullPath { get; } = invalidFullName;
  public string? InvalidRelativePath { get; } = invalidFullName.StartsWith(baseFolder)
      ? Path.GetDirectoryName(invalidFullName.Remove(0, baseFolder.Length + 1))
      : Path.GetDirectoryName(invalidFullName);
  public string InvalidFileName { get; } = Path.GetFileName(invalidFullName);
  public IEnumerable<IRenamingSuggestion> Suggestions =>
      _SuggestionsList?.Take(SuggestionsCount) ?? Enumerable.Empty<IRenamingSuggestion>();
  private List<IRenamingSuggestion>? _SuggestionsList { get; set; } = new(suggestions.Take(MAX_SUGGESTIONS_COUNT));
  private bool _IsCommited
  {
    get => __isCommited;
    set
    {
      __isCommited = value;
      _SuggestionsList = null;
    }
  }
  private bool __isCommited;
  private int __suggestionsCount;
  public int SuggestionsCount
  {
    get => __suggestionsCount;
    set => __suggestionsCount = value > MAX_SUGGESTIONS_COUNT ? MAX_SUGGESTIONS_COUNT : value;
  }

  public ITransactionResponse Commit(int suggestionIndex)
  {
    if (_IsCommited || _SuggestionsList is null)
    {
      return new TransactionResponse(false, "Transaction is already commited.");
    }

    if (suggestionIndex < 0 || suggestionIndex >= _SuggestionsList.Count)
    {
      return new TransactionResponse(false, "Index out of range.");
    }

    return Commit(_SuggestionsList[suggestionIndex]);
  }
  public ITransactionResponse Commit(IRenamingSuggestion suggestion)
  {
    if (_IsCommited || _SuggestionsList is null)
    {
      return new TransactionResponse(false, "Transaction is already commited.");
    }

    if (!_SuggestionsList.Contains(suggestion))
    {
      return new TransactionResponse(false, $"Suggestion \"{suggestion}\" does not belong into this transaction. Transaction was not committed.");
    }
    try
    {
      File.Move(suggestion.InvalidFullPath, suggestion.FullPath, true);
      Logger.Debug($"File name {suggestion.InvalidFullPath} was corrected to {Path.GetFileName(suggestion.FullPath)}", _logger);
    }
    catch (Exception e)
    {
      Logger.Warn(e, _logger);
      return new TransactionResponse(false, $"File {suggestion.InvalidFullPath} could not be renamed to {suggestion.FullPath}");
    }
    _IsCommited = true;
    return new TransactionResponse(true);
  }
  public ITransactionResponse Commit(string newFileName)
  {
    if (_IsCommited || _SuggestionsList is null)
    {
      return new TransactionResponse(false, "Transaction is already commited.");
    }

    string fileNameWithoutExt = Path.GetFileNameWithoutExtension(newFileName);
    var suggestion = new Suggestion(InvalidFullPath, fileNameWithoutExt, 0);
    if (suggestion.InvalidFullPath == suggestion.FullPath)
    {
      return new TransactionResponse(false, $"New file name must be different from invalid file name ({InvalidFileName})");
    }

    _SuggestionsList.Add(suggestion);
    return Commit(suggestion);
  }

  /// <summary>
  /// Deletes the invalid file and marks the transaction as committed.
  /// </summary>
  /// <returns></returns>
  public ITransactionResponse Delete()
  {
    if (_IsCommited || _SuggestionsList is null)
    {
      return new TransactionResponse(false, "Transaction is already commited.");
    }

    if (!File.Exists(InvalidFullPath))
    {
      return new TransactionResponse(false, $"File \"{InvalidFullPath}\" does not exist.");
    }

    try
    {
      File.Delete(InvalidFullPath);
    }
    catch (Exception e)
    {
      return new TransactionResponse(false, $"File \"{InvalidFullPath}\" could not be deleted ({e.Message})");
    }

    _IsCommited = true;
    return new TransactionResponse(true);
  }
}
