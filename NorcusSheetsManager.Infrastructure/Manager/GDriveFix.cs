using System.Text.RegularExpressions;

namespace NorcusSheetsManager.Infrastructure.Manager;

public static class GDriveFix
{
  private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

  /// <summary>
  /// Fixes file names in a Google Drive folder — removes the version marker from "file (1).pdf".
  /// </summary>
  /// <param name="allowMultipleVersions">If true, keeps only the file with the highest version marker. If false and multiple files share the same base name, they are left untouched.</param>
  public static void FixAllFiles(string directory, SearchOption searchOption, bool allowMultipleVersions, params string[] extensionFilter)
  {
    var files = new List<string>();
    if (extensionFilter.Length == 0)
    {
      extensionFilter = new[] { "*" };
    }

    foreach (string ext in extensionFilter)
    {
      files.AddRange(Directory.GetFiles(directory, "*." + ext.TrimStart('.'), searchOption));
    }
    files = files.Where(f => Regex.IsMatch(f, GDriveFile.VerPattern)).ToList();

    if (files.Count > 0)
    {
      _logger.Debug("Found {Count} GDrive files with version in their names.", files.Count);
    }
    foreach (string file in files)
    {
      if (File.Exists(file))
      {
        FixFile(file, allowMultipleVersions);
      }
    }
  }

  /// <summary>
  /// Fixes a file name in a Google Drive folder — removes the version marker from "file (1).pdf".
  /// </summary>
  /// <param name="allowMultipleVersions">If true, keeps only the file with the highest version marker. If false and multiple files share the same base name, they are left untouched.</param>
  /// <returns>New file name</returns>
  public static string FixFile(string fullFileName, bool allowMultipleVersions)
  {
    string newFileName = fullFileName;
    var file = new GDriveFile(fullFileName);

    string pattern = Regex.Escape(Path.Combine(file.Directory, file.FileNameNoVerNoExt))
        + "((" + GDriveFile.VerPattern + ")|(\\.))"
        + file.Extension.TrimStart('.');

    var allVersions = Directory.GetFiles(file.Directory, $"{file.FileNameNoVerNoExt}*{file.Extension}")
        .Where(f => Regex.IsMatch(f, pattern))
        .ToList();

    if (allVersions.Count > 1 && allowMultipleVersions || allVersions.Count == 0)
    {
      return newFileName;
    }

    if (allVersions.Count == 1)
    {
      try
      {
        string renamedFile = File.Exists(fullFileName) ? fullFileName : allVersions[0];
        File.Move(renamedFile, file.FullFileNameNoVer);
        newFileName = file.FullFileNameNoVer;
        _logger.Debug("File {File} renamed to {NewName}.", renamedFile, file.FullFileNameNoVer);
      }
      catch (Exception e)
      {
        _logger.Error(e, "File {File} couldn't be renamed to {NewName}.", fullFileName, file.FullFileNameNoVer);
      }
    }
    else
    {
      var sortedVersions = allVersions.Select(f => new GDriveFile(f)).OrderBy(f => f.Version).ToList();
      for (int i = 0; i < sortedVersions.Count - 1; i++)
      {
        if (File.Exists(sortedVersions[i].FullFileName))
        {
          File.Delete(sortedVersions[i].FullFileName);
          _logger.Debug("File {File} deleted.", sortedVersions[i].FullFileName);
        }
      }
      GDriveFile lastFile = sortedVersions.Last();
      try
      {
        File.Move(lastFile.FullFileName, lastFile.FullFileNameNoVer);
        newFileName = lastFile.FullFileNameNoVer;
        _logger.Debug("File {File} renamed to {NewName}.", lastFile.FullFileName, lastFile.FullFileNameNoVer);
      }
      catch (Exception e)
      {
        _logger.Error(e, "File {File} couldn't be renamed to {NewName}.", lastFile.FullFileName, lastFile.FullFileNameNoVer);
      }
    }
    return newFileName;
  }

  public class GDriveFile
  {
    public const string VerPattern = "\\s\\(\\d+\\)\\.";
    public string FileName { get; private set; }
    public string FileNameNoVer { get; private set; }
    public string FileNameNoVerNoExt { get; set; }
    public string FullFileNameNoVer => Path.Combine(Directory, FileNameNoVer);
    public string Extension { get; set; }
    public string FullFileName
    {
      get => __fullFilePath;
      set
      {
        __fullFilePath = value;
        _SetProperties();
      }
    }
    private string __fullFilePath;
    public string Directory { get; set; } = "";
    public int Version { get; private set; }

    public GDriveFile(string fullFileName)
    {
      FullFileName = fullFileName;
    }

    public static bool HasGDriveVersion(string fullFilePath) =>
        Regex.Match(Path.GetFileName(fullFilePath), VerPattern).Success;

    private void _SetProperties()
    {
      FileName = Path.GetFileName(FullFileName);
      Extension = Path.GetExtension(FullFileName);
      Directory = Path.GetDirectoryName(FullFileName) ?? "";
      Match basicMatch = Regex.Match(FileName, VerPattern);

      if (!basicMatch.Success)
      {
        FileNameNoVer = FileName;
        FileNameNoVerNoExt = Path.GetFileNameWithoutExtension(FullFileName);
        Version = 0;
        return;
      }

      Version = int.Parse(Regex.Match(basicMatch.Value, "\\d+").Value);
      FileNameNoVer = Regex.Replace(FileName, VerPattern, ".");
      FileNameNoVerNoExt = Path.GetFileNameWithoutExtension(FileNameNoVer);
    }
  }
}
