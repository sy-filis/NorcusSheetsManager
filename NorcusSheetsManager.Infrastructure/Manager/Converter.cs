using System.Diagnostics;
using ImageMagick;
using Microsoft.Extensions.Logging;
using NorcusSheetsManager.Application.Configuration;

namespace NorcusSheetsManager.Infrastructure.Manager;

public class Converter
{
  private readonly ILogger<Converter> _logger;
  private readonly MagickReadSettings _magickReadSettings;
  public MagickFormat OutFileFormat { get; }
  public string MultiPageDelimiter { get; }
  public int MultiPageCounterLength { get; }
  public int MultiPageInitNumber { get; }
  public int DPI { get; }
  public bool TransparentBackground { get; }
  public bool CropImage { get; }

  static Converter()
  {
    if (OperatingSystem.IsWindows())
    {
      MagickNET.SetGhostscriptDirectory(AppContext.BaseDirectory);
    }
  }

  public Converter(AppConfig config, ILogger<Converter> logger)
  {
    _logger = logger;
    ConverterSettings s = config.Converter;
    OutFileFormat = s.OutFileFormat;
    MultiPageDelimiter = s.MultiPageDelimiter;
    MultiPageCounterLength = s.MultiPageCounterLength;
    MultiPageInitNumber = s.MultiPageInitNumber;
    DPI = s.DPI;
    TransparentBackground = s.TransparentBackground;
    CropImage = s.CropImage;

    _magickReadSettings = new MagickReadSettings
    {
      Density = new Density(DPI),
      Format = MagickFormat.Pdf
    };
  }

  /// <summary>
  /// Converts <paramref name="pdfFile"/> into images.
  /// </summary>
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

    _logger.LogDebug("Converting {File} into {Format} image.", pdfFile.FullName, OutFileFormat);

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
        _logger.LogWarning(e, "Could not read {File}; sleeping 100 ms and retrying.", pdfFile.FullName);
        Thread.Sleep(100);
        try
        {
          fileBytes = File.ReadAllBytes(pdfFile.FullName);
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "File {File} could not be opened.", pdfFile.FullName);
        }
      }

      if (fileBytes is null)
      {
        return Enumerable.Empty<FileInfo>();
      }

      images.Read(fileBytes, _magickReadSettings);

      if (images.Count == 1)
      {
        if (_IsBlankPage(images[0]))
        {
          _logger.LogDebug("Page of {File} appears blank; skipping image output.", pdfFile.FullName);
        }
        else
        {
          _ModifyImage(images[0]);
          images[0].Write(outFileNoExt + outExtension, OutFileFormat);
          result.Add(new FileInfo(outFileNoExt + outExtension));
        }
      }
      else if (images.Count > 1)
      {
        int page = MultiPageInitNumber;
        foreach (IMagickImage<byte> image in images)
        {
          image.Format = OutFileFormat;
          if (_IsBlankPage(image))
          {
            _logger.LogDebug("Page {Page} of {File} appears blank; skipping image output.", page, pdfFile.FullName);
            page++;
            continue;
          }
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
    _logger.LogDebug("{File} was converted into {Count} {Format} image(s).", pdfFile.FullName, result.Count, OutFileFormat);
    return result;
  }

  public bool TryGetPdfPageCount(FileInfo pdfFile, out int pageCount)
  {
    // PdfInfo.Create(pdfFile).PageCount throws for some reason, so we invoke Ghostscript directly:
    string fullPath = pdfFile.FullName.Replace("\\", "/");
    string gsExecutable = OperatingSystem.IsWindows()
        ? Path.Combine(AppContext.BaseDirectory, "gswin64c.exe")
        : "gs";
    var startInfo = new ProcessStartInfo
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

  /// <summary>
  /// An empty PDF page renders as a single-colour bitmap — every pixel is the background
  /// colour, so the per-channel standard deviation is essentially zero. Real content
  /// (even a single staff line) has visible variance. Threshold is in Q8 quantum units
  /// (0..255); ~0.8% allows for JPEG dithering while staying well below any actual ink.
  /// </summary>
  private static bool _IsBlankPage(IMagickImage image)
  {
    const double stdDevThreshold = 2.0;

    IStatistics stats = image.Statistics();
    foreach (PixelChannel channel in image.Channels)
    {
      IChannelStatistics? channelStats = stats.GetChannel(channel);
      if (channelStats is not null && channelStats.StandardDeviation > stdDevThreshold)
      {
        return false;
      }
    }
    return true;
  }
}
