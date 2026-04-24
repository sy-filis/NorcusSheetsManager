using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Timers;
using System.Xml.Linq;
using ImageMagick.Configuration;
using ImageMagick.Formats;
using NorcusSheetsManager.API;
using NorcusSheetsManager.NameCorrector;
using static System.Net.Mime.MediaTypeNames;

namespace NorcusSheetsManager;

internal class Manager
{
  private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
  private Converter _Converter { get; set; }
  private List<FileSystemWatcher> _FileSystemWatchers { get; set; }
  private bool _IsWatcherEnabled { get; set; }
  private bool _ScanningInProgress { get; set; }
  public AppConfig Config { get; private set; }
  public Corrector NameCorrector { get; private set; }
  public Manager()
  {
    Config = ConfigLoader.Load();
    if (string.IsNullOrEmpty(Config.Converter.SheetsPath))
    {
      Exception e = new ArgumentNullException(nameof(Config.Converter.SheetsPath));
      Logger.Error(e, _logger);
      throw e;
    }

    _Converter = new Converter()
    {
      OutFileFormat = Config.Converter.OutFileFormat,
      MultiPageDelimiter = Config.Converter.MultiPageDelimiter,
      MultiPageCounterLength = Config.Converter.MultiPageCounterLength,
      MultiPageInitNumber = Config.Converter.MultiPageInitNumber,
      DPI = Config.Converter.DPI,
      TransparentBackground = Config.Converter.TransparentBackground,
      CropImage = Config.Converter.CropImage
    };

    _FileSystemWatchers = _CreateFileSystemWatchers();

    DatabaseConnection db = Config.ApiServer.DbConnection;
    IDbLoader sqlLoader = File.Exists(db.Database) && Path.GetExtension(db.Database) == ".txt"
      ? new DbFileLoader(db.Database) { UserId = db.UserId }
      : new MySQLLoader(db.Server, db.Port, db.Database, db.UserId, db.Password);
    NameCorrector = new Corrector(sqlLoader, Config.Converter.SheetsPath, Config.Converter.WatchedExtensions);

    if (Config.ApiServer.RunServer)
    {
      List<(Type type, object instance)> singletons = new()
              {
                  (typeof(Corrector), NameCorrector),
                  (typeof(Manager), this)
              };
      Server.Initialize(Config.ApiServer.Port, Config.ApiServer.Key, singletons);
      Server.Start();
      Logger.Debug($"API server started (port {Config.ApiServer.Port}).", _logger);
    }
  }
  /// <summary>
  /// Creates a FileSystemWatcher for each sheets folder. Only the top level of each folder is watched.
  /// </summary>
  private List<FileSystemWatcher> _CreateFileSystemWatchers()
  {
    List<FileSystemWatcher> _fileSystemWatchers = new();
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
        EnableRaisingEvents = true
      };
      foreach (string ext in Config.Converter.WatchedExtensions)
      {
        watcher.Filters.Add("*" + ext);
      }
      watcher.Changed += Watcher_Changed;
      watcher.Created += Watcher_Created;
      watcher.Renamed += Watcher_Renamed;
      watcher.Deleted += Watcher_Deleted;

      _fileSystemWatchers.Add(watcher);
    }
    return _fileSystemWatchers;
  }

  public void StartWatching(bool verbose = false)
  {
    _IsWatcherEnabled = true;
    if (verbose)
    {
      Logger.Debug($"File system watcher started.", _logger);
    }
  }
  public void StopWatching(bool verbose = false)
  {
    _IsWatcherEnabled = false;
    if (verbose)
    {
      Logger.Debug($"File system watcher stoppped.", _logger);
    }
  }
  public void AutoFullScan(double interval, int repeats)
  {
    var timer = new System.Timers.Timer(interval);
    int hitCount = 0;
    timer.Elapsed += (sender, e) =>
    {
      Logger.Debug("Autoscan:", _logger);
      if (_ScanningInProgress)
      {
        Logger.Debug("Autoscan skipped (scanning already running).", _logger);
        return;
      }
      hitCount++;
      FullScan();

      if (hitCount >= repeats)
      {
        var senderTimer = sender as System.Timers.Timer;
        senderTimer?.Stop();
        senderTimer?.Dispose();
        Logger.Debug("Autoscan finished.", _logger);
        return;
      }
    };
    timer.Start();
  }
  public void FullScan()
  {
    StopWatching();
    _ScanningInProgress = true;
    Logger.Debug($"Scanning all PDF files in {Config.Converter.SheetsPath}.", _logger);
    if (Config.Converter.FixGDriveNaming)
    {
      _FixAllGoogleFiles();
    }

    IEnumerable<FileInfo> pdfFiles = _GetPdfFiles(false);

    Logger.Debug($"Found {pdfFiles.Count()} PDF files in {Config.Converter.SheetsPath}.", _logger);

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
      Logger.Debug($"{convertCounter} files converted to {Config.Converter.OutFileFormat}.", _logger);
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
    Logger.Debug($"Deep scanning all PDF files in {Config.Converter.SheetsPath}.", _logger);
    if (Config.Converter.FixGDriveNaming)
    {
      _FixAllGoogleFiles();
    }

    IEnumerable<FileInfo> pdfFiles = _GetPdfFiles(false);
    IEnumerable<FileInfo> archivePdfFiles = _GetPdfFiles(true);

    if (!Config.Converter.MovePdfToSubfolder)
    {
      Logger.Debug($"Found {pdfFiles.Count()} PDF files in {Config.Converter.SheetsPath}.", _logger);
    }
    else
    {
      Logger.Debug($"Found {pdfFiles.Count() + archivePdfFiles.Count()} PDF files in {Config.Converter.SheetsPath} " +
          $"and \"{Config.Converter.PdfSubfolder}\" subfolders.", _logger);
    }

    int convertCounter = 0;
    foreach (FileInfo pdfFile in pdfFiles)
    {
      if (!_Converter.TryGetPdfPageCount(pdfFile, out int pageCount))
      {
        Logger.Warn($"Unable to get page count of file {pdfFile.FullName}.", _logger);
        continue;
      }
      ;

      int fileCount = _GetImagesForPdf(pdfFile).Length;
      if (pageCount == fileCount)
      {
        continue;
      }

      Logger.Debug($"File {pdfFile.FullName} has {pageCount} page(s), but {fileCount} file(s).", _logger);
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
          Logger.Warn($"Unable to get page count of file {pdfFile.FullName}.", _logger);
          continue;
        }
        ;

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

        Logger.Debug($"File {pdfFile.FullName} has {pageCount} page(s), but {fileCount} file(s).", _logger);
        File.Move(pdfFile.FullName, pdfFileParentDir.FullName);
        bool converted = _DeleteOlderAndConvert(pdfFileParentDir, true);
        if (converted)
        {
          convertCounter++;
        }
      }
    }

    Logger.Debug($"{convertCounter} file(s) converted to {Config.Converter.OutFileFormat}.", _logger);
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
    Logger.Debug($"Force converting all PDF files in {Config.Converter.SheetsPath}.", _logger);
    if (Config.Converter.FixGDriveNaming)
    {
      _FixAllGoogleFiles();
    }

    IEnumerable<FileInfo> pdfFiles = _GetPdfFiles(false);
    IEnumerable<FileInfo> archivePdfFiles = _GetPdfFiles(true);

    if (!Config.Converter.MovePdfToSubfolder)
    {
      Logger.Debug($"Found {pdfFiles.Count()} PDF files in {Config.Converter.SheetsPath}.", _logger);
    }
    else
    {
      Logger.Debug($"Found {pdfFiles.Count() + archivePdfFiles.Count()} PDF files " +
          $"in {Config.Converter.SheetsPath} and \"{Config.Converter.PdfSubfolder}\" subfolders.", _logger);
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

    Logger.Debug($"{convertCounter} file(s) converted to {Config.Converter.OutFileFormat}.", _logger);
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

    Logger.Debug($"Detected: {e.OldFullPath} was renamed to {e.FullPath}.", _logger);

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
    Logger.Debug($"Detected: {fullPath} was created.", _logger);
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

    Logger.Debug($"Detected: {pdfFile} has changed.", _logger);
    _DeleteOlderAndConvert(pdfFile);
  }
  private void Watcher_Deleted(object sender, FileSystemEventArgs e)
  {
    if (!_IsWatcherEnabled)
    {
      return;
    }

    string fullPath = e.FullPath;
    Logger.Debug($"Detected: {fullPath} was deleted.", _logger);
    //if (Config.Converter.FixGDriveNaming && Regex.IsMatch(fullPath, GDriveFix.GDriveFile.VerPattern))
    //{
    //    StopWatching();
    //    fullPath = _FixGoogleFile(fullPath);
    //    StartWatching();
    //}

    //FileInfo file = new FileInfo(fullPath);
    //if (file.Extension == ".pdf")
    //    _DeleteOlderAndConvert(file, true);
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
  /// <param name="pdfFile"></param>
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
          Logger.Debug($"Image {image.FullName} was deleted" + (imgsAreOlder ? " (found newer PDF)." : "."), _logger);
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
      else
      {
        return false;
      }
    }
    catch (Exception ex)
    {
      Logger.Error(ex, _logger);
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

        Logger.Debug($"File {Path.GetFileName(image.FullName)} was renamed to {newPath}", _logger);
      }
    }
    catch (Exception ex)
    {
      Logger.Error(ex, _logger);
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
      Logger.Debug($"File {sourceFile} was moved to \"{Config.Converter.PdfSubfolder}\" subfolder.", _logger);
    }
    catch (Exception e)
    {
      Logger.Warn($"File {sourceFile} could not be moved to \"{Config.Converter.PdfSubfolder}\" subfolder.", _logger);
      Logger.Warn(e, _logger);
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
