using NorcusSheetsManager.Application.Abstractions.Models;

namespace NorcusSheetsManager.Infrastructure.NameCorrector;

internal class Suggestion : IRenamingSuggestion
{
  public string InvalidFullPath { get; }
  public string FullPath { get; }
  public string FileName { get; }
  public bool FileExists { get; }
  public double Distance { get; }
  public Suggestion(string invalidFullPath, string suggestedNameWithoutExt, double distance)
  {
    InvalidFullPath = invalidFullPath;
    FileName = suggestedNameWithoutExt;
    string ext = Path.GetExtension(invalidFullPath);
    FullPath = Path.Combine(Path.GetDirectoryName(invalidFullPath) ?? "", suggestedNameWithoutExt + ext);
    FileExists = File.Exists(FullPath);
    Distance = distance;
  }

  public override string ToString()
  {
    return Path.GetFileNameWithoutExtension(InvalidFullPath) + " -> " + Path.GetFileNameWithoutExtension(FullPath);
  }
}
