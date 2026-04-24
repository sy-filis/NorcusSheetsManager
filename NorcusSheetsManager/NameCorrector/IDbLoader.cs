using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NorcusSheetsManager.NameCorrector;

internal interface IDbLoader
{
  string Server { get; set; }
  string Database { get; set; }
  string UserId { get; set; }
  string Password { get; set; }
  string ConnectionString { get; }
  IEnumerable<string> GetSongNames();
  IEnumerable<INorcusUser> GetUsers();
  Task ReloadDataAsync();
}
