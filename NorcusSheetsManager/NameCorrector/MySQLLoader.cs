using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MySqlConnector;

namespace NorcusSheetsManager.NameCorrector;

internal class MySQLLoader : IDbLoader
{
  private static readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
  public string Server { get; set; }
  public ushort Port { get; set; }
  public string Database { get; set; }
  public string UserId { get; set; }
  public string Password { get; set; }
  public string ConnectionString => new MySqlConnectionStringBuilder
  {
    Server = Server,
    Port = Port,
    Database = Database,
    UserID = UserId,
    Password = Password,
  }.ConnectionString;
  private List<string> _Songs { get; set; } = new();
  private List<NorcusUser> _Users { get; set; } = new();
  public MySQLLoader(string server, ushort port, string database, string userId, string password)
  {
    Server = server;
    Port = port;
    Database = database;
    UserId = userId;
    Password = password;
  }

  public IEnumerable<string> GetSongNames()
  {
    if (_Songs.Count == 0)
    {
      ReloadDataAsync().Wait();
    }

    return _Songs;
  }
  public IEnumerable<INorcusUser> GetUsers()
  {
    if (_Users.Count == 0)
    {
      ReloadDataAsync().Wait();
    }

    return _Users;
  }

  public async Task ReloadDataAsync()
  {
    if (string.IsNullOrEmpty(Server) || string.IsNullOrEmpty(Database))
    {
      _Songs = new();
      _Users = new();
      return;
    }

    try
    {
      using var connection = new MySqlConnection(ConnectionString);
      await connection.OpenAsync();
      _Songs = await _GetSongs(connection);
      _Users = await _GetUsers(connection);
    }
    catch (Exception e)
    {
      Logger.Error(e, _logger);
      _Songs = new();
      _Users = new();
    }
  }

  private async Task<List<string>> _GetSongs(MySqlConnection connection)
  {
    List<string> songs = new();
    using var command = new MySqlCommand("SELECT filename FROM songs", connection);
    using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
      songs.Add(reader.GetString(0));
    }
    return songs;
  }

  private async Task<List<NorcusUser>> _GetUsers(MySqlConnection connection)
  {
    List<NorcusUser> users = new();
    using var command = new MySqlCommand("SELECT uuid, name, email, folder FROM musicians", connection);
    using var reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
      var norcusUser = new NorcusUser()
      {
        Guid = reader.GetGuid(0),
        Name = reader.GetString(1),
        Email = reader.GetString(2),
        Folder = reader.GetString(3),
      };
      users.Add(norcusUser);
    }
    return users;
  }
}
