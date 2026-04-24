using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NorcusSheetsManager.Application.Abstractions.Services;
using NorcusSheetsManager.Application.Configuration;

namespace NorcusSheetsManager.Infrastructure.Manager;

internal class Manager : IScanService, IWatcherControl
{
  private readonly ILogger<Manager> _logger;
  private readonly Converter _Converter;
  private readonly List<FileSystemWatcher> _FileSystemWatchers;
  private bool _IsWatcherEnabled;
  private bool _ScanningInProgress;
  public AppConfig Config { get; }

  public Manager(AppConfig config, Converter converter, ILogger<Manager> logger)
  {
    Config = config;
    _logger = logger;
    if (string.IsNullOrEmpty(Config.Converter.SheetsPath))
    {
      Exception e = new ArgumentNullException(nameof(Config.Converter.SheetsPath));
      _logger.LogError(e, "SheetsPath is not configured.");
      throw e;
    }

    _Converter = converter;
    _FileSystemWatchers = _CreateFileSystemWatchers();
  }

  /// <summary>
  /// Creates a FileSystemWatcher for each sheets folder. Only the top level of each folder is watched.
  /// </summary>
  private List<FileSystemWatcher> _CreateFileSystemWatchers()
  {
    var watchers = new List<FileSystemWatcher>();
    string[] directories = Directory.GetDirectories(Config.Converter.SheetsPath!);
    foreach (string dir in directories)
    {
      var watcher = new FileSystemWatcher
      {
        Path = dir,
        IncludeSubdirectories = false,
        NotifyFilter = NotifyFilters.CreationTime |
          NotifyFilters.DirectoryName |
          NotifyFilters.FileName |
          NotifyFilters.LastWrite,
        EnableRaisingEvents = true,
      };
      foreach (string ext in Config.Converter.WatchedExtensions)
      {
        watcher.Filters.Add("*" + ext);
      }
      watcher.Changed += Watcher_Changed;
      watcher.Created += Watcher_Created;
      watcher.Renamed += Watcher_Renamed;
      watcher.Deleted += Watcher_Deleted;

      watchers.Add(watcher);
    }
    return watchers;
  }

  public void StartWatching(bool verbose = false)
  {
    _IsWatcherEnabled = true;
    if (verbose)
    {
      _logger.LogDebug("File system watcher started.");
    }
  }

  public void StopWatching(bool verbose = false)
  {
    _IsWatcherEnabled = false;
    if (verbose)
    {
      _logger.LogDebug("File system watcher stopped.");
    }
  }

  public void AutoFullScan(double interval, int repeats)
  {
    var timer = new System.Timers.Timer(interval);
    int hitCount = 0;
    timer.Elapsed += (sender, e) =>
    {
      _logger.LogDebug("Autoscan tick.");
      if (_ScanningInProgress)
      {
        _logger.LogDebug("Autoscan skipped (scanning already running).");
        return;
      }
      hitCount++;
      FullScan();

      if (hitCount >= repeats)
      {
        var senderTimer = sender as System.Timers.Timer;
        senderTimer?.Stop();
        senderTimer?.Dispose();
        _logger.LogInformation("Autoscan finished after {Repeats} iterations.", repeats);
        return;
      }
    };
    timer.Start();
  }

  public void FullScan()
  {
    StopWatching();
    _ScanningInProgress = true;
    _logger.LogInformation("Scanning all PDF files in {Path}.", Config.Converter.SheetsPath);
    if (Config.Converter.FixGDriveNaming)
    {
      _FixAllGoogleFiles();
    }

    IEnumerable<FileInfo> pdfFiles = _GetPdfFiles(false);

    _logger.LogInformation("Found {Count} PDF files in {Path}.", pdfFiles.Count(), Config.Converter.SheetsPath);

    int convertCounter = 0;
    foreach (FileInfo pdfFile in pdfFiles)
    {
      bool converted = _DeleteOlderAndConvert(pdfFile);
      if (converted)
      {
        convertCounter++;
      }
    }
    if (convertCounter > 0)
    {
      _logger.LogInformation("{Count} files converted to {Format}.", convertCounter, Config.Converter.OutFileFormat);
    }

    _ScanningInProgress = false;
    StartWatching();
  }

  /// <summary>
  /// Scans every PDF and checks that the image-file count matches the PDF page count. Reconverts the mismatches.
  /// </summary>
  public void DeepScan()
  {
    StopWatching();
    _ScanningInProgress = true;
    _logger.LogInformation("Deep scanning all PDF files in {Path}.", Config.Converter.SheetsPath);
    if (Config.Converter.FixGDriveNaming)
    {
      _FixAllGoogleFiles();
    }

    IEnumerable<FileInfo> pdfFiles = _GetPdfFiles(false);
    IEnumerable<FileInfo> archivePdfFiles = _GetPdfFiles(true);

    if (!Config.Converter.MovePdfToSubfolder)
    {
      _logger.LogInformation("Found {Count} PDF files in {Path}.", pdfFiles.Count(), Config.Converter.SheetsPath);
    }
    else
    {
      _logger.LogInformation(
          "Found {Count} PDF files in {Path} and {Subfolder} subfolders.",
          pdfFiles.Count() + archivePdfFiles.Count(),
          Config.Converter.SheetsPath,
          Config.Converter.PdfSubfolder);
    }

    int convertCounter = 0;
    foreach (FileInfo pdfFile in pdfFiles)
    {
      if (!_Converter.TryGetPdfPageCount(pdfFile, out int pageCount))
      {
        _logger.LogWarning("Unable to get page count of file {File}.", pdfFile.FullName);
        continue;
      }

      int fileCount = _GetImagesForPdf(pdfFile).Length;
      if (pageCount == fileCount)
      {
        continue;
      }

      _logger.LogDebug("File {File} has {Pages} page(s), but {Images} file(s).", pdfFile.FullName, pageCount, fileCount);
      bool converted = _DeleteOlderAndConvert(pdfFile, true);
      if (converted)
      {
        convertCounter++;
      }
    }

    if (Config.Converter.MovePdfToSubfolder)
    {
      foreach (FileInfo pdfFile in archivePdfFiles)
      {
        if (!_Converter.TryGetPdfPageCount(pdfFile, out int pageCount))
        {
          _logger.LogWarning("Unable to get page count of file {File}.", pdfFile.FullName);
          continue;
        }

        DirectoryInfo? parent = Directory.GetParent(pdfFile.Directory!.FullName);
        if (parent is null)
        {
          continue;
        }
        var pdfFileParentDir = new FileInfo(Path.Combine(parent.FullName, pdfFile.Name));
        int fileCount = _GetImagesForPdf(pdfFileParentDir).Length;
        if (pageCount == fileCount)
        {
          continue;
        }

        _logger.LogDebug("File {File} has {Pages} page(s), but {Images} file(s).", pdfFile.FullName, pageCount, fileCount);
        File.Move(pdfFile.FullName, pdfFileParentDir.FullName);
        bool converted = _DeleteOlderAndConvert(pdfFileParentDir, true);
        if (converted)
        {
          convertCounter++;
        }
      }
    }

    _logger.LogInformation("{Count} file(s) converted to {Format}.", convertCounter, Config.Converter.OutFileFormat);
    _ScanningInProgress = false;
    StartWatching();
  }

  /// <summary>
  /// Converts all PDF files into images.
  /// </summary>
  public void ForceConvertAll()
  {
    StopWatching();
    _ScanningInProgress = true;
    _logger.LogInformation("Force converting all PDF files in {Path}.", Config.Converter.SheetsPath);
    if (Config.Converter.FixGDriveNaming)
    {
      _FixAllGoogleFiles();
    }

    IEnumerable<FileInfo> pdfFiles = _GetPdfFiles(false);
    IEnumerable<FileInfo> archivePdfFiles = _GetPdfFiles(true);

    if (!Config.Converter.MovePdfToSubfolder)
    {
      _logger.LogInformation("Found {Count} PDF files in {Path}.", pdfFiles.Count(), Config.Converter.SheetsPath);
    }
    else
    {
      _logger.LogInformation(
          "Found {Count} PDF files in {Path} and {Subfolder} subfolders.",
          pdfFiles.Count() + archivePdfFiles.Count(),
          Config.Converter.SheetsPath,
          Config.Converter.PdfSubfolder);
      // If moving PDFs to a subfolder is enabled, pull them from the subfolder back up to the parent folder.
      foreach (FileInfo archivePdf in archivePdfFiles)
      {
        if (archivePdf.DirectoryName is null)
        {
          continue;
        }
        DirectoryInfo? parent = Directory.GetParent(archivePdf.DirectoryName);
        if (parent is null)
        {
          continue;
        }
        var pdfInParentDir = new FileInfo(Path.Combine(parent.FullName, archivePdf.Name));
        if (!pdfInParentDir.Exists)
        {
          File.Move(archivePdf.FullName, pdfInParentDir.FullName);
        }
      }
      // Refresh the PDF list in Config.Converter.SheetsPath:
      pdfFiles = _GetPdfFiles(false);
    }

    int convertCounter = 0;
    foreach (FileInfo pdfFile in pdfFiles)
    {
      bool converted = _DeleteOlderAndConvert(pdfFile, true);
      if (converted)
      {
        convertCounter++;
      }
    }

    _logger.LogInformation("{Count} file(s) converted to {Format}.", convertCounter, Config.Converter.OutFileFormat);
    _ScanningInProgress = false;
    StartWatching();
  }

  private void _FixAllGoogleFiles()
  {
    bool isWatcherActive = _IsWatcherEnabled;
    if (isWatcherActive)
    {
      StopWatching();
    }

    GDriveFix.FixAllFiles(Config.Converter.SheetsPath!, SearchOption.AllDirectories, false, Config.Converter.WatchedExtensions);
    if (isWatcherActive)
    {
      StartWatching();
    }
  }

  private string _FixGoogleFile(string fullFileName)
  {
    bool isWatcherActive = _IsWatcherEnabled;
    if (isWatcherActive)
    {
      StopWatching();
    }

    string newFileName = GDriveFix.FixFile(fullFileName, false);
    if (isWatcherActive)
    {
      StartWatching();
    }

    return newFileName;
  }

  private void Watcher_Renamed(object sender, RenamedEventArgs e)
  {
    if (!_IsWatcherEnabled)
    {
      return;
    }

    _logger.LogDebug("Detected: {OldPath} was renamed to {NewPath}.", e.OldFullPath, e.FullPath);

    // If this was a GDrive-rename fixup, the PDF needs to be reconverted:
    if (Path.GetExtension(e.FullPath) == ".pdf" && Regex.IsMatch(e.OldFullPath, GDriveFix.GDriveFile.VerPattern))
    {
      _DeleteOlderAndConvert(new FileInfo(e.FullPath), true);
      return;
    }

    if (e.OldName is null || e.Name is null)
    {
      return;
    }
    FileInfo[] images = _GetImagesForPdf(new FileInfo(e.OldFullPath));
    _RenameImages(images, e.OldName, e.Name);
  }

  private void Watcher_Created(object sender, FileSystemEventArgs e)
  {
    if (!_IsWatcherEnabled)
    {
      return;
    }

    string fullPath = e.FullPath;
    _logger.LogDebug("Detected: {Path} was created.", fullPath);
    if (Config.Converter.FixGDriveNaming && Regex.IsMatch(fullPath, GDriveFix.GDriveFile.VerPattern))
    {
      fullPath = _FixGoogleFile(fullPath);
    }

    var file = new FileInfo(fullPath);
    if (file.Extension == ".pdf")
    {
      _DeleteOlderAndConvert(file);
    }
  }

  private void Watcher_Changed(object sender, FileSystemEventArgs e)
  {
    if (!_IsWatcherEnabled)
    {
      return;
    }

    var pdfFile = new FileInfo(e.FullPath);
    if (pdfFile.Extension != ".pdf" || !pdfFile.Exists)
    {
      return;
    }

    _logger.LogDebug("Detected: {File} has changed.", pdfFile);
    _DeleteOlderAndConvert(pdfFile);
  }

  private void Watcher_Deleted(object sender, FileSystemEventArgs e)
  {
    if (!_IsWatcherEnabled)
    {
      return;
    }

    _logger.LogDebug("Detected: {Path} was deleted.", e.FullPath);
  }

  private FileInfo[] _GetImagesForPdf(FileInfo pdfFile)
  {
    string dir = pdfFile.Directory!.FullName;
    string name = Path.GetFileNameWithoutExtension(pdfFile.Name);
    string ext = "." + Config.Converter.OutFileFormat.ToString().ToLower();
    string pattern = $".*{Regex.Escape(name)}({Config.Converter.MultiPageDelimiter}\\d*)?(\\s\\(\\d+\\))?\\{ext}";

    FileInfo[] foundFiles = Directory.GetFiles(dir, $"{name}*{ext}", SearchOption.TopDirectoryOnly)
        .Where(path => Regex.IsMatch(path, pattern))
        .Select(f => new FileInfo(f))
        .ToArray();
    return foundFiles;
  }

  /// <summary>
  /// If no images exist for the PDF, or they are older than the PDF, deletes them and reconverts the PDF.
  /// </summary>
  /// <param name="forceDeleteAndConvert">Deletes every file belonging to the PDF and always reconverts.</param>
  /// <returns>True if a conversion ran.</returns>
  private bool _DeleteOlderAndConvert(FileInfo pdfFile, bool forceDeleteAndConvert = false)
  {
    try
    {
      FileInfo[] images = _GetImagesForPdf(pdfFile);
      bool imgsAreOlder = images.Any(i => i.LastWriteTimeUtc < pdfFile.LastWriteTimeUtc);
      if (imgsAreOlder || forceDeleteAndConvert)
      {
        foreach (FileInfo image in images)
        {
          File.Delete(image.FullName);
          _logger.LogDebug("Image {File} was deleted (newerPdf={NewerPdf}).", image.FullName, imgsAreOlder);
        }
      }
      if (images.Length == 0 || imgsAreOlder || forceDeleteAndConvert)
      {
        IEnumerable<FileInfo> createdImages = _Converter.Convert(pdfFile);
        _SyncFileTimes(pdfFile, createdImages);
        if (Config.Converter.MovePdfToSubfolder)
        {
          _MovePdfToSubfolder(pdfFile);
        }
        return true;
      }
      return false;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to convert {File}.", pdfFile.FullName);
      return false;
    }
  }

  private void _RenameImages(FileInfo[] images, string oldName, string newName)
  {
    try
    {
      string oldNameNoExt = Path.GetFileNameWithoutExtension(oldName);
      string newNameNoExt = Path.GetFileNameWithoutExtension(newName);
      foreach (FileInfo image in images)
      {
        string? dir = Path.GetDirectoryName(image.FullName);
        if (dir is null)
        {
          continue;
        }
        string name = Path.GetFileName(image.FullName);
        string newPath = Path.Combine(dir, name.Replace(oldNameNoExt, newNameNoExt));

        if (image.FullName == newPath)
        {
          return;
        }

        if (File.Exists(newPath))
        {
          File.Delete(newPath);
        }

        File.Move(image.FullName, newPath);
        _logger.LogDebug("File {OldName} was renamed to {NewPath}.", Path.GetFileName(image.FullName), newPath);
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to rename images from {OldName} to {NewName}.", oldName, newName);
    }
  }

  private void _SyncFileTimes(FileInfo source, IEnumerable<FileInfo> targets)
  {
    foreach (FileInfo target in targets)
    {
      target.CreationTime = source.CreationTime;
      target.CreationTimeUtc = source.CreationTimeUtc;
      target.LastAccessTime = source.LastAccessTime;
      target.LastAccessTimeUtc = source.LastAccessTimeUtc;
      target.LastWriteTime = source.LastWriteTime;
      target.LastWriteTimeUtc = source.LastWriteTimeUtc;
    }
  }

  private void _MovePdfToSubfolder(FileInfo pdfFile)
  {
    if (!pdfFile.Exists)
    {
      return;
    }

    string sourceFile = pdfFile.FullName;
    string? pdfDir = Path.GetDirectoryName(pdfFile.FullName);
    if (pdfDir is null)
    {
      return;
    }
    string newPath = Path.Combine(pdfDir, Config.Converter.PdfSubfolder);
    if (!Directory.Exists(newPath))
    {
      Directory.CreateDirectory(newPath);
    }

    string targetFile = Path.Combine(newPath, pdfFile.Name);
    try
    {
      File.Move(sourceFile, targetFile, true);
      _logger.LogDebug("File {File} was moved to {Subfolder} subfolder.", sourceFile, Config.Converter.PdfSubfolder);
    }
    catch (Exception e)
    {
      _logger.LogWarning(e, "File {File} could not be moved to {Subfolder} subfolder.", sourceFile, Config.Converter.PdfSubfolder);
    }
  }

  private IEnumerable<FileInfo> _GetPdfFiles(bool filesInPdfSubfolder)
  {
    string[] directories = Directory.GetDirectories(Config.Converter.SheetsPath!);
    var pdfFiles = new List<FileInfo>();

    foreach (string dir in directories)
    {
      string dirx = filesInPdfSubfolder ? Path.Combine(dir, Config.Converter.PdfSubfolder) : dir;
      if (!Directory.Exists(dirx))
      {
        continue;
      }

      pdfFiles.AddRange(Directory.GetFiles(dirx, "*.pdf", SearchOption.TopDirectoryOnly)
          .Select(f => new FileInfo(f)));
    }
    return pdfFiles;
  }
}
