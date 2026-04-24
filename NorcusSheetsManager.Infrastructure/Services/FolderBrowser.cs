using NorcusSheetsManager.Application.Abstractions.Services;
using NorcusSheetsManager.Application.Configuration;

namespace NorcusSheetsManager.Infrastructure.Services;

internal sealed class FolderBrowser(AppConfig config) : IFolderBrowser
{
  public IEnumerable<string> GetSheetFolders()
  {
    string? basePath = config.Converter.SheetsPath;
    if (string.IsNullOrEmpty(basePath) || !Directory.Exists(basePath))
    {
      return Array.Empty<string>();
    }

    return Directory.GetDirectories(basePath)
        .Select(Path.GetFileName)
        .Where(d => !string.IsNullOrEmpty(d) && !d.StartsWith("."))
        .Cast<string>()
        .ToList();
  }
}
