using ImageMagick;

namespace NorcusSheetsManager;

public class AppConfig
{
  public ConverterSettings Converter { get; set; } = new();
  public ApiServerSettings ApiServer { get; set; } = new();
}

public class ConverterSettings
{
  public string? SheetsPath { get; set; } = AppContext.BaseDirectory;
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
  public string[] WatchedExtensions { get; set; } = new[] { ".pdf", ".jpg", ".png", ".txt" };
}

public class ApiServerSettings
{
  public bool RunServer { get; set; } = true;
  public int Port { get; set; } = 4434;
  public string Key { get; set; } = "";
  public DatabaseConnection DbConnection { get; set; } = new();
}

public class DatabaseConnection
{
  public string Server { get; set; } = "localhost";
  public ushort Port { get; set; } = 3306;
  public string Database { get; set; } = "database";
  public string UserId { get; set; } = "user";
  public string Password { get; set; } = "";
}
