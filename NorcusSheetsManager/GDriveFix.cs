using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NorcusSheetsManager;

public static class GDriveFix
{
  private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
  /// <summary>
  /// Opraví názvy souborů ve složce Google Disku - vymaže označení verze souboru "soubor (1).pdf".
  /// </summary>
  /// <param name="directory"></param>
  /// <param name="includeSubdirectories"></param>
  /// <param name="allowMultipleVersions">Pokud true, ponechá pouze soubor s nejvyšším označením verze. Když false, pokud je více souborů se stejným názvem, nebude je přejmenovávat.</param>
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
      Logger.Debug($"Found {files.Count} GDrive Files with version in their names.", _logger);
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
  /// Opraví název souboru ve složce Google Disku - vymaže označení verze souboru "soubor (1).pdf".
  /// </summary>
  /// <param name="fullFileName"></param>
  /// <param name="allowMultipleVersions">Pokud true, ponechá pouze soubor s nejvyšším označením verze. Když false, pokud je více souborů se stejným názvem, nebude je přejmenovávat.</param>
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
        Logger.Debug($"File {renamedFile} renamed to {file.FullFileNameNoVer}.", _logger);
      }
      catch (Exception e)
      {
        Logger.Error($"File {fullFileName} couldn't be renamed to {file.FullFileNameNoVer}!", _logger);
        Logger.Error(e, _logger);
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
          Logger.Debug($"File {sortedVersions[i].FullFileName} deleted.", _logger);
        }
      }
      GDriveFile lastFile = sortedVersions.Last();
      try
      {
        File.Move(lastFile.FullFileName, lastFile.FullFileNameNoVer);
        newFileName = lastFile.FullFileNameNoVer;
        Logger.Debug($"File {lastFile.FullFileName} renamed to {lastFile.FullFileNameNoVer}.", _logger);
      }
      catch (Exception e)
      {
        Logger.Error($"File {lastFile.FullFileName} couldn't be renamed to {lastFile.FullFileNameNoVer}!", _logger);
        Logger.Error(e, _logger);
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
