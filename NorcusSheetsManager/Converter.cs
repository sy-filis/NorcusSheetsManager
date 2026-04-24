using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;
using ImageMagick.Formats;
using NLog;

namespace NorcusSheetsManager;

public class Converter
{
  private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
  private readonly MagickReadSettings _magickReadSettings;
  /// <summary>
  /// default = Png
  /// </summary>
  public MagickFormat OutFileFormat { get; set; } = MagickFormat.Png;
  /// <summary>
  /// default = "-"
  /// </summary>
  public string MultiPageDelimiter { get; set; } = "-";
  /// <summary>
  /// Počet číslic (default 3 => 003)
  /// </summary>
  public int MultiPageCounterLength { get; set; } = 3;
  /// <summary>
  /// Určuje počáteční index (default = 1)
  /// </summary>
  public int MultiPageInitNumber { get; set; } = 1;
  /// <summary>
  /// default = 200
  /// </summary>
  public int? DPI
  {
    get => System.Convert.ToInt32(_magickReadSettings.Density?.X); set => _magickReadSettings.Density = value.HasValue ? new Density((int)value) : null;
  }
  /// <summary>
  /// default = false
  /// </summary>
  public bool TransparentBackground { get; set; } = false;
  /// <summary>
  /// Oříznout obrázek dle obsahu. Default = true;
  /// </summary>
  public bool CropImage { get; set; } = true;
  static Converter()
  {
    if (OperatingSystem.IsWindows())
    {
      MagickNET.SetGhostscriptDirectory(AppContext.BaseDirectory);
    }
  }
  public Converter()
  {
    _magickReadSettings = new MagickReadSettings()
    {
      Density = new Density(200),
      Format = MagickFormat.Pdf
    };
  }
  /// <summary>
  /// Converts <paramref name="pdfFile"/> into images.
  /// </summary>
  /// <param name="pdfFile"></param>
  /// <returns>Files created</returns>
  /// <exception cref="FormatException"></exception>
  public IEnumerable<FileInfo> Convert(FileInfo pdfFile)
  {
    if (!pdfFile.Exists)
    {
      return Enumerable.Empty<FileInfo>();
    }

    if (pdfFile.Extension.ToLower() != ".pdf")
    {
      throw new FormatException("Input file must be PDF");
    }

    Logger.Debug($"Converting {pdfFile.FullName} into {OutFileFormat} image.", _logger);

    string outFileNoExt = Path.Combine(pdfFile.Directory!.FullName, Path.GetFileNameWithoutExtension(pdfFile.FullName));
    string outExtension = "." + OutFileFormat.ToString().ToLower();
    var result = new List<FileInfo>();

    using (var images = new MagickImageCollection())
    {
      byte[]? fileBytes = null;
      try
      {
        fileBytes = File.ReadAllBytes(pdfFile.FullName);
      }
      catch (IOException e)
      {
        Logger.Warn(e, _logger);
        Logger.Warn("I will sleep for 100ms and try again...", _logger);
        Thread.Sleep(100);
        try
        {
          fileBytes = File.ReadAllBytes(pdfFile.FullName);
        }
        catch (Exception ex)
        {
          Logger.Error($"File {pdfFile.FullName} could not be opened.", _logger);
          Logger.Error(ex, _logger);
        }
      }

      if (fileBytes is null)
      {
        return Enumerable.Empty<FileInfo>();
      }

      images.Read(fileBytes, _magickReadSettings);

      if (images.Count == 1)
      {
        _ModifyImage(images[0]);
        images[0].Write(outFileNoExt + outExtension, OutFileFormat);
        result.Add(new FileInfo(outFileNoExt + outExtension));
      }
      else if (images.Count > 1)
      {
        int page = MultiPageInitNumber;
        foreach (var image in images)
        {
          image.Format = OutFileFormat;
          _ModifyImage(image);
          string fname = outFileNoExt + MultiPageDelimiter + _GetCounter(page) + outExtension;
          image.Write(fname, OutFileFormat);
          result.Add(new FileInfo(fname));
          page++;
        }
      }
      else
      {
        return Enumerable.Empty<FileInfo>();
      }
    }
    Logger.Debug($"{pdfFile.FullName} was converted into {result.Count} {OutFileFormat} image"
        + (result.Count > 1 ? "s." : "."), _logger);
    return result;
  }
  public bool TryGetPdfPageCount(FileInfo pdfFile, out int pageCount)
  {
    // Metoda PdfInfo.Create(pdfFile).PageCount z nějakého důvodu hází chybu. Použiji tedy Ghostscript napřímo:
    string fullPath = pdfFile.FullName.Replace("\\", "/");
    string gsExecutable = OperatingSystem.IsWindows()
        ? Path.Combine(AppContext.BaseDirectory, "gswin64c.exe")
        : "gs";
    var startInfo = new ProcessStartInfo()
    {
      FileName = gsExecutable,
      Arguments = $"-q -dQUIET -dSAFER -dBATCH -dNOPAUSE -dNOPROMPT --permit-file-read=\"{fullPath}\" -sPDFPassword=\"\" -c \"({fullPath}) (r) file runpdfbegin pdfpagecount = quit\"",
      UseShellExecute = false,
      RedirectStandardOutput = true,
      CreateNoWindow = true
    };
    var proc = Process.Start(startInfo);
    if (proc is null)
    {
      pageCount = 0;
      return false;
    }
    bool success = int.TryParse(proc.StandardOutput.ReadToEnd(), out pageCount);

    return success && pageCount > 0;
  }
  private string _GetCounter(int num)
  {
    if (num.ToString().Length > MultiPageCounterLength)
    {
      return num.ToString();
    }

    string counter = "";
    for (int i = 0; i < MultiPageCounterLength; i++)
    {
      counter += "0";
    }
    counter += num;
    counter = counter.Substring(counter.Length - MultiPageCounterLength, MultiPageCounterLength);
    return counter;
  }
  private void _ModifyImage(IMagickImage image)
  {
    if (!TransparentBackground)
    {
      image.Alpha(AlphaOption.Deactivate);
    }

    if (CropImage)
    {
      image.Trim();
      uint newWidth = System.Convert.ToUInt32(image.Width * 1.01);
      uint newHeight = System.Convert.ToUInt32(image.Height * 1.01);
      image.Extent(newWidth, newHeight, Gravity.Center);
    }
  }
}
