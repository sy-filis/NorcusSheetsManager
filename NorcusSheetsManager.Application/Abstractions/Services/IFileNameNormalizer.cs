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
}
