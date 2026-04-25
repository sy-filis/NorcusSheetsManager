using NorcusSheetsManager.Application.Abstractions.Models;

namespace NorcusSheetsManager.Infrastructure.NameCorrector;

internal class Suggestion(
    string invalidFullPath,
    string suggestedNameWithoutExt,
    double distance,
    IEnumerable<string> watchedExtensions,
    char multiPageDelimiter) : IRenamingSuggestion
{
  public string InvalidFullPath { get; } = invalidFullPath;
  public string FileName { get; } = suggestedNameWithoutExt;
  public double Distance { get; } = distance;
  public string FullPath { get; } = Path.Combine(
      Path.GetDirectoryName(invalidFullPath) ?? "",
      suggestedNameWithoutExt + Path.GetExtension(invalidFullPath));

  /// <summary>
  /// True when the song's folder contains any file (in any watched extension) whose
  /// normalized base name equals <see cref="FileName"/> — either the canonical
  /// singleton "<c>{FileName}.{ext}</c>" or a numbered variant
  /// "<c>{FileName}{delimiter}NNN.{ext}</c>". Recomputed on every read so the FE
  /// sees current filesystem state without restarting the cached transaction.
  /// </summary>
  public bool FileExists
  {
    get
    {
      string? folder = Path.GetDirectoryName(InvalidFullPath);
      if (folder is null)
      {
        return false;
      }
      string[] candidates;
      try
      {
        candidates = Directory.GetFiles(folder, FileName + "*", SearchOption.TopDirectoryOnly);
      }
      catch (DirectoryNotFoundException)
      {
        return false;
      }
      foreach (string path in candidates)
      {
        string ext = Path.GetExtension(path);
        if (!watchedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
        {
          continue;
        }
        string baseName = Path.GetFileNameWithoutExtension(path);
        if (string.Equals(baseName, FileName, StringComparison.OrdinalIgnoreCase))
        {
          return true;
        }
        if (baseName.Length > FileName.Length + 1
            && baseName.StartsWith(FileName + multiPageDelimiter, StringComparison.OrdinalIgnoreCase))
        {
          string tail = baseName.Substring(FileName.Length + 1);
          if (tail.All(char.IsDigit))
          {
            return true;
          }
        }
      }
      return false;
    }
  }

  public override string ToString()
  {
    return Path.GetFileNameWithoutExtension(InvalidFullPath) + " -> " + Path.GetFileNameWithoutExtension(FullPath);
  }
}
