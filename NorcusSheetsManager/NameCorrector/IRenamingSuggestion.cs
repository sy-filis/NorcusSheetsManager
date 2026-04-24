using System.Text.Json.Serialization;

namespace NorcusSheetsManager.NameCorrector;

internal interface IRenamingSuggestion
{
  /// <summary>
  /// Plná cesta k novému názvu souboru
  /// </summary>
  [JsonIgnore]
  string FullPath { get; }
  /// <summary>
  /// Plná cesta k chybnému názvu souboru
  /// </summary>
  [JsonIgnore]
  string InvalidFullPath { get; }
  /// <summary>
  /// Navrhovaný název nového souboru bez přípony
  /// </summary>
  string FileName { get; }
  bool FileExists { get; }
}
