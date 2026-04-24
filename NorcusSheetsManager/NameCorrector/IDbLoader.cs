using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NorcusSheetsManager.NameCorrector;

internal interface IDbLoader
{
  string Server { get; init; }
  string Database { get; init; }
  string UserId { get; init; }
  string Password { get; init; }
  string ConnectionString { get; }
  IEnumerable<string> GetSongNames();
  IEnumerable<INorcusUser> GetUsers();
  Task ReloadDataAsync();
}
