using ImageMagick;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NorcusSheetsManager
{
    public interface IConfig
    {
        string SheetsPath { get; set; }
        bool AutoScan { get; set; }
        MagickFormat OutFileFormat { get; set; }
        string MultiPageDelimiter { get; set; }
        int MultiPageCounterLength { get; set;}
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
        public string Server { get; set; }
        public string Database { get; set; }
        public string UserId { get; set; }
        public string Password { get; set; }
    }
    public interface IAPISettings
    {
        public bool RunServer { get; set; }
        public int Port { get; set; }
        public string Key { get; set; }
    }
}
