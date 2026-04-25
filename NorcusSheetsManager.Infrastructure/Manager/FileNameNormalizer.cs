using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NorcusSheetsManager.Application.Abstractions.Services;
using NorcusSheetsManager.Application.Configuration;

namespace NorcusSheetsManager.Infrastructure.Manager;

internal sealed class FileNameNormalizer(
    AppConfig config,
    ILogger<FileNameNormalizer> logger) : IFileNameNormalizer
{
  private static readonly Regex _NumberedPattern = new(@"^(.+)-(\d+)\.([^.]+)$", RegexOptions.Compiled);
  private static readonly Regex _SimplePattern = new(@"^(.+)\.([^.]+)$", RegexOptions.Compiled);
  private const string _TempSuffix = ".tmp";

  public void NormalizeFolder(string folderPath)
  {
    if (!Directory.Exists(folderPath))
    {
      logger.LogDebug("NormalizeFolder skipped: folder {Folder} does not exist.", folderPath);
      return;
    }

    _RecoverTempFiles(folderPath);

    HashSet<string> imageExts = _GetImageExtensions();
    var grouped = _EnumerateImages(folderPath, imageExts)
        .Select(f => (File: f, Parsed: _Parse(f.Name)))
        .Where(p => p.Parsed.HasValue)
        .GroupBy(p => p.Parsed!.Value.Base, StringComparer.OrdinalIgnoreCase);

    int touched = 0, renamed = 0;
    foreach (var group in grouped)
    {
      int before = renamed;
      renamed += _NormalizeGroup(folderPath, group.Key, group.Select(p => p.File).ToList());
      if (renamed > before)
      {
        touched++;
      }
    }

    logger.LogDebug("NormalizeFolder({Folder}) finished: {Touched} group(s) touched, {Renamed} file(s) renamed.", folderPath, touched, renamed);
  }

  public void NormalizeSong(string folderPath, string baseName)
  {
    if (!Directory.Exists(folderPath))
    {
      logger.LogDebug("NormalizeSong skipped: folder {Folder} does not exist.", folderPath);
      return;
    }
    if (string.IsNullOrEmpty(baseName))
    {
      return;
    }

    _RecoverTempFiles(folderPath);

    HashSet<string> imageExts = _GetImageExtensions();
    List<FileInfo> files = _EnumerateImages(folderPath, imageExts)
        .Where(f =>
        {
          var parsed = _Parse(f.Name);
          return parsed.HasValue && string.Equals(parsed.Value.Base, baseName, StringComparison.OrdinalIgnoreCase);
        })
        .ToList();

    if (files.Count == 0)
    {
      return;
    }

    _NormalizeGroup(folderPath, baseName, files);
  }

  /// <summary>
  /// Returns the number of files renamed.
  /// </summary>
  private int _NormalizeGroup(string folderPath, string baseName, List<FileInfo> files)
  {
    IReadOnlyList<(string From, string To)> renames = _PlanRenames(folderPath, baseName, files);
    if (renames.Count == 0)
    {
      return 0;
    }

    // Phase 1: move each source to <source>.tmp
    var phase1 = new List<(string Tmp, string Final, string OriginalName)>();
    foreach (var (from, to) in renames)
    {
      string tmp = from + _TempSuffix;
      try
      {
        File.Move(from, tmp);
        phase1.Add((tmp, to, Path.GetFileName(from)));
      }
      catch (FileNotFoundException ex)
      {
        logger.LogDebug(ex, "Phase 1 skipped (file missing): {From}.", from);
      }
      catch (DirectoryNotFoundException ex)
      {
        logger.LogDebug(ex, "Phase 1 skipped (folder gone): {From}.", from);
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Phase 1 rename failed: {From} → {Tmp}. Aborting group {Base} in {Folder}.", from, tmp, baseName, folderPath);
        return 0;
      }
    }

    // Phase 2: move each .tmp to its final name.
    int renamedCount = 0;
    foreach (var (tmp, final, original) in phase1)
    {
      try
      {
        File.Move(tmp, final);
        logger.LogInformation("Normalized {From} → {To} in {Folder}.", original, Path.GetFileName(final), folderPath);
        renamedCount++;
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Phase 2 rename failed: {Tmp} → {Final}. File left as {Tmp} for next-run recovery.", tmp, final, tmp);
      }
    }

    return renamedCount;
  }

  /// <summary>
  /// Computes the (from, to) rename list for a group. Pure: no IO.
  /// </summary>
  private IReadOnlyList<(string From, string To)> _PlanRenames(string folderPath, string baseName, IReadOnlyList<FileInfo> files)
  {
    int counterLength = config.Converter.MultiPageCounterLength;
    int initNumber = config.Converter.MultiPageInitNumber;

    var parsed = files
        .Select(f => (File: f, Parsed: _Parse(f.Name)))
        .Where(p => p.Parsed.HasValue)
        .Select(p => (p.File, P: p.Parsed!.Value))
        .ToList();

    if (parsed.Count == 0)
    {
      return Array.Empty<(string, string)>();
    }

    if (parsed.Count == 1)
    {
      var only = parsed[0];
      string targetName = $"{baseName}.{only.P.Ext}";
      string targetPath = Path.Combine(folderPath, targetName);
      if (string.Equals(only.File.FullName, targetPath, StringComparison.OrdinalIgnoreCase))
      {
        return Array.Empty<(string, string)>();
      }
      return [(only.File.FullName, targetPath)];
    }

    // 2+ files — sort and assign sequential numbers.
    var sorted = parsed
        .OrderBy(p => p.P.Page.HasValue ? 1 : 0)
        .ThenBy(p => p.P.Page ?? -1)
        .ThenBy(p => p.P.Ext, StringComparer.OrdinalIgnoreCase)
        .ToList();

    var result = new List<(string, string)>();
    for (int i = 0; i < sorted.Count; i++)
    {
      int n = initNumber + i;
      string targetName = $"{baseName}-{n.ToString().PadLeft(counterLength, '0')}.{sorted[i].P.Ext}";
      string targetPath = Path.Combine(folderPath, targetName);
      if (string.Equals(sorted[i].File.FullName, targetPath, StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }
      result.Add((sorted[i].File.FullName, targetPath));
    }
    return result;
  }

  /// <summary>
  /// Restores any leftover &lt;original&gt;.tmp files from a previous failed pass.
  /// If the original slot is free, drops the .tmp suffix. If the slot is taken, leaves the .tmp alone and warns.
  /// </summary>
  private void _RecoverTempFiles(string folderPath)
  {
    string[] tmpFiles;
    try
    {
      tmpFiles = Directory.GetFiles(folderPath, "*" + _TempSuffix, SearchOption.TopDirectoryOnly);
    }
    catch (DirectoryNotFoundException)
    {
      return;
    }

    foreach (string tmp in tmpFiles)
    {
      string restored = tmp.Substring(0, tmp.Length - _TempSuffix.Length);
      // restored is the original full path (e.g., ".../song-001.jpg.tmp" → ".../song-001.jpg")
      if (File.Exists(restored))
      {
        logger.LogWarning("Cannot recover temp file {Tmp}: target {Restored} already exists.", tmp, restored);
        continue;
      }
      try
      {
        File.Move(tmp, restored);
        logger.LogInformation("Recovered temp file {Tmp} → {Restored}.", Path.GetFileName(tmp), Path.GetFileName(restored));
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Failed to recover temp file {Tmp} → {Restored}.", tmp, restored);
      }
    }
  }

  private IEnumerable<FileInfo> _EnumerateImages(string folder, HashSet<string> imageExts)
  {
    FileInfo[] files;
    try
    {
      files = new DirectoryInfo(folder).GetFiles("*", SearchOption.TopDirectoryOnly);
    }
    catch (DirectoryNotFoundException)
    {
      return Array.Empty<FileInfo>();
    }
    return files.Where(f => imageExts.Contains(f.Extension));
  }

  private HashSet<string> _GetImageExtensions()
  {
    return new HashSet<string>(
        config.Converter.WatchedExtensions.Where(e => !string.Equals(e, ".pdf", StringComparison.OrdinalIgnoreCase)),
        StringComparer.OrdinalIgnoreCase);
  }

  /// <summary>
  /// Parses "song-001.jpg" → ("song", 1, "jpg") greedy on the numbered pattern,
  /// falling back to "song.jpg" → ("song", null, "jpg"). Returns null if no extension.
  /// </summary>
  private static (string Base, int? Page, string Ext)? _Parse(string fileName)
  {
    Match m = _NumberedPattern.Match(fileName);
    if (m.Success)
    {
      return (m.Groups[1].Value, int.Parse(m.Groups[2].Value), m.Groups[3].Value);
    }
    Match m2 = _SimplePattern.Match(fileName);
    if (m2.Success)
    {
      return (m2.Groups[1].Value, null, m2.Groups[2].Value);
    }
    return null;
  }

  /// <summary>
  /// Public-ish helper for callers (Manager) that have a filename and want the parsed base name.
  /// Returns null if the filename can't be parsed.
  /// </summary>
  internal static string? GetBaseName(string fileName)
  {
    return _Parse(fileName)?.Base;
  }
}
