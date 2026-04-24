using System.Text.Json.Serialization;

namespace NorcusSheetsManager.NameCorrector;

/// <summary>
/// Adds <see cref="InvalidRelativePath"/> on top of <see cref="IRenamingTransactionBase"/>.
/// </summary>
internal interface IRenamingTransaction : IRenamingTransactionBase
{
  [JsonPropertyName("TransactionGuid")]
  new Guid Guid { get; }

  [JsonPropertyName("Folder")]
  string? InvalidRelativePath { get; }

  /// <summary>
  /// Invalid file name.
  /// </summary>
  new string InvalidFileName { get; }
  new IEnumerable<IRenamingSuggestion> Suggestions { get; }
}
