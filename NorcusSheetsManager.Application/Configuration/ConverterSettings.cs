using ImageMagick;

namespace NorcusSheetsManager.Application.Configuration;

public class ConverterSettings
{
  public string? SheetsPath
  {
    get;
    set
    {
      if (value == string.Empty)
      {
        throw new ArgumentException("SheetsPath cannot be empty.", nameof(SheetsPath));
      }
      field = value;
    }
  }
  public bool AutoScan { get; set; } = true;
  public MagickFormat OutFileFormat { get; set; } = MagickFormat.Png;
  public string MultiPageDelimiter { get; set; } = "-";
  public int MultiPageCounterLength { get; set; } = 3;
  public int MultiPageInitNumber { get; set; } = 1;
  public int DPI { get; set; } = 300;
  public bool TransparentBackground { get; set; } = false;
  public bool CropImage { get; set; } = true;
  public bool MovePdfToSubfolder { get; set; } = true;
  public string PdfSubfolder { get; set; } = "Archiv PDF";
  public bool FixGDriveNaming { get; set; } = true;
  public string[] WatchedExtensions { get; set; } = [".pdf", ".jpg", ".jpeg", ".png", ".gif"];
}
