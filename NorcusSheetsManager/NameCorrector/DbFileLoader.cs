using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NorcusSheetsManager.NameCorrector;

internal class DbFileLoader(string fileName) : IDbLoader
{
  public string Server { get; init; }
  public string Database { get; init; }
  public string UserId { get; init; }
  public string Password { get; init; }

  public string ConnectionString => $"Server={Server}; Database={Database}; User Id={UserId}; Password={Password};";
  private string[] _songs = File.ReadAllLines(fileName);

  public IEnumerable<string> GetSongNames() => _songs;

  public async Task ReloadDataAsync()
  {
    _songs = File.ReadAllLines(fileName);
  }

  public IEnumerable<INorcusUser> GetUsers()
  {
    string[] splitUser = UserId.Split(";");
    Guid userGuid = Guid.Empty;
    try
    {
      userGuid = new Guid(splitUser[0]);
    }
    catch { }
    var user = new NorcusUser() { Guid = userGuid, Folder = splitUser[1] };
    return new List<NorcusUser>() { user };
  }
}
