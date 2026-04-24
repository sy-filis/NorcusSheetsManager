using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using ImageMagick;

namespace NorcusSheetsManager;

public static class ConfigLoader
{
  private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

  private static string GetDefaultConfigFilePath()
  {
    string? exePath = Environment.ProcessPath;
    if (string.IsNullOrEmpty(exePath))
    {
      return Path.Combine(AppContext.BaseDirectory, "NorcusSheetsManagerCfg.xml");
    }

    string dir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
    string name = Path.GetFileNameWithoutExtension(exePath);
    return Path.Combine(dir, name + "Cfg.xml");
  }
  public static IConfig Load() => Load(GetDefaultConfigFilePath());
  public static IConfig Load(string configFilePath)
  {
    _logger.Info("Loading config file: " + configFilePath);
    if (!System.IO.File.Exists(configFilePath))
    {
      _logger.Warn("File was not found ({0}), creating default Config", configFilePath);
      Config newConfig = _GetDefaultConfig();
      _Save(newConfig);
      return Load(configFilePath);
    }

    var serializer = new System.Xml.Serialization.XmlSerializer(typeof(Config));
    System.IO.FileStream file = System.IO.File.OpenRead(configFilePath);
    Config? deserialized = null;
    try
    { deserialized = serializer.Deserialize(file) as Config; }
    catch (Exception e) { _logger.Warn(e, "Deserialization failed"); }
    finally { file.Close(); }

    if (deserialized != null)
    {
      _Save(deserialized); // tímto zajistím uložení aktuální verze Configu v případě, že načtený Config byl starší verze.
    }

    return deserialized ?? _GetDefaultConfig();
  }

  private static void _Save(Config config) => Save(GetDefaultConfigFilePath(), config);
  private static void Save(string configFilePath, Config config)
  {
    _logger.Debug("Saving config file to {0}", configFilePath);

    var serializer =
        new System.Xml.Serialization.XmlSerializer(typeof(Config));

    System.IO.FileStream file = System.IO.File.Create(configFilePath);

    serializer.Serialize(file, config);
    file.Close();
  }

  private static Config _GetDefaultConfig() => new Config();

  public class Config : IConfig
  {
    public string? SheetsPath { get; set; } = AppContext.BaseDirectory;
    public bool AutoScan { get; set; } = true;
    public MagickFormat OutFileFormat { get; set; } = MagickFormat.Png;
    public string MultiPageDelimiter { get; set; } = "-";
    public int MultiPageCounterLength { get; set; } = 3;
    public int MultiPageInitNumber { get; set; } = 1;
    public int DPI { get; set; } = 200;
    public bool TransparentBackground { get; set; } = false;
    public bool CropImage { get; set; } = true;
    public bool MovePdfToSubfolder { get; set; } = true;
    public string PdfSubfolder { get; set; } = "Archiv PDF";
    public bool FixGDriveNaming { get; set; } = true;
    public string[] WatchedExtensions { get; set; } = new[] { ".pdf", ".jpg", ".png", ".txt" };
    public APIServerSettings APISettings { get; set; } = new APIServerSettings();
    public DatabaseConnection DbConnection { get; set; } = new DatabaseConnection();
    public class APIServerSettings : IAPISettings
    {
      public bool RunServer { get; set; } = true;
      public int Port { get; set; } = 4434;
      public string Key { get; set; } = "";
    }
    public class DatabaseConnection : IDbConnection
    {
      public string Server { get; set; } = "server";
      public ushort Port { get; set; } = 3306;
      public string Database { get; set; } = "database";
      public string UserId { get; set; } = "user";
      public string Password { get; set; } = "password";
    }
  }
}
