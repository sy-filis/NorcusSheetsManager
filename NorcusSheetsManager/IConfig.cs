using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ImageMagick;

namespace NorcusSheetsManager;

public interface IConfig
{
  string SheetsPath { get; set; }
  bool AutoScan { get; set; }
  MagickFormat OutFileFormat { get; set; }
  string MultiPageDelimiter { get; set; }
  int MultiPageCounterLength { get; set; }
  int MultiPageInitNumber { get; set; }
  int DPI { get; set; }
  bool TransparentBackground { get; set; }
  bool CropImage { get; set; }
  bool MovePdfToSubfolder { get; set; }
  string PdfSubfolder { get; set; }
  bool FixGDriveNaming { get; set; }
  string[] WatchedExtensions { get; set; }
  ConfigLoader.Config.APIServerSettings APISettings { get; set; }
  ConfigLoader.Config.DatabaseConnection DbConnection { get; set; }
}
public interface IDbConnection
{
  string Server { get; set; }
  string Database { get; set; }
  string UserId { get; set; }
  string Password { get; set; }
}
public interface IAPISettings
{
  bool RunServer { get; set; }
  int Port { get; set; }
  string Key { get; set; }
}
