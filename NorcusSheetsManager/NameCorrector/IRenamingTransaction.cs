using System.Text.Json.Serialization;

namespace NorcusSheetsManager.NameCorrector;

/// <summary>
/// Oproti <see cref="IRenamingTransactionBase"/> má navíc <see cref="InvalidRelativePath"/>.
/// </summary>
internal interface IRenamingTransaction : IRenamingTransactionBase
{
  [JsonPropertyName("TransactionGuid")]
  new Guid Guid { get; }

  [JsonPropertyName("Folder")]
  string? InvalidRelativePath { get; }

  /// <summary>
  /// Název chybného souboru.
  /// </summary>
  new string InvalidFileName { get; }
  new IEnumerable<IRenamingSuggestion> Suggestions { get; }
}
