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

  /// <summary>
  /// Number of currently-invalid files, optionally filtered to a top-level
  /// subfolder of <see cref="BaseSheetsFolder"/>. Reads an in-memory index;
  /// no filesystem walk per call.
  /// </summary>
  int GetInvalidCount(string? sheetsSubfolder = null);

  /// <summary>
  /// Notify the index that a file appeared. Only matters when the basename is
  /// invalid under the current song list — valid creations are ignored.
  /// </summary>
  void OnFileCreated(string fullPath);

  /// <summary>
  /// Notify the index that a file was renamed. Removes the old path from the
  /// invalid set; checks the new path and adds it if invalid. Drops any
  /// cached transaction tied to the old path.
  /// </summary>
  void OnFileRenamed(string oldFullPath, string newFullPath);

  /// <summary>
  /// Notify the index that a file was removed. Drops any cached transaction
  /// tied to the path.
  /// </summary>
  void OnFileDeleted(string fullPath);

  /// <summary>
  /// Walk every top-level subfolder of <see cref="BaseSheetsFolder"/> and
  /// rebuild the invalid-files index from scratch. Called at startup, on
  /// every song-list change, and after each <see cref="IScanService"/> scan
  /// (where the watcher was paused so incremental updates were missed).
  /// No-op while the song list is empty — otherwise every file would be
  /// flagged as invalid.
  /// </summary>
  void RebuildInvalidIndex();
}
