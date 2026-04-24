using System.Text.Json.Serialization;

namespace NorcusSheetsManager.NameCorrector;

internal interface IRenamingSuggestion
{
  /// <summary>
  /// Full path to the new file name.
  /// </summary>
  [JsonIgnore]
  string FullPath { get; }
  /// <summary>
  /// Full path to the invalid file name.
  /// </summary>
  [JsonIgnore]
  string InvalidFullPath { get; }
  /// <summary>
  /// Suggested new file name without extension.
  /// </summary>
  string FileName { get; }
  bool FileExists { get; }
}
