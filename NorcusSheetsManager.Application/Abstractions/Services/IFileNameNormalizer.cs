namespace NorcusSheetsManager.Application.Abstractions.Services;

public interface IFileNameNormalizer
{
  /// <summary>
  /// Normalizes every base-name group in <paramref name="folderPath"/>.
  /// </summary>
  void NormalizeFolder(string folderPath);

  /// <summary>
  /// Normalizes a single base-name group within <paramref name="folderPath"/>.
  /// </summary>
  void NormalizeSong(string folderPath, string baseName);

  /// <summary>
  /// Parses <paramref name="fileName"/> and returns the base-name portion, or null if it can't be parsed.
  /// </summary>
  string? GetBaseName(string fileName);
}
