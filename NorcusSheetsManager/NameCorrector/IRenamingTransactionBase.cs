using System.Text.Json.Serialization;

namespace NorcusSheetsManager.NameCorrector;

internal interface IRenamingTransactionBase
{
  [JsonPropertyName("TransactionGuid")]
  Guid Guid { get; }

  [JsonIgnore]
  string InvalidFullPath { get; }

  /// <summary>
  /// Invalid file name.
  /// </summary>
  string InvalidFileName { get; }
  IEnumerable<IRenamingSuggestion> Suggestions { get; }

  ITransactionResponse Commit(int suggestionIndex);
  ITransactionResponse Commit(IRenamingSuggestion suggestion);
  ITransactionResponse Commit(string newFileName);
}
