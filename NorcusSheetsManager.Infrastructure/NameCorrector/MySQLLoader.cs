using Microsoft.Extensions.Logging;
using MySqlConnector;
using NorcusSheetsManager.Application.Abstractions.Models;

namespace NorcusSheetsManager.Infrastructure.NameCorrector;

internal class MySQLLoader(
    string server,
    ushort port,
    string database,
    string userId,
    string password,
    ILogger<MySQLLoader> logger) : IDbLoader
{
  public string Server { get; init; } = server;
  public ushort Port { get; init; } = port;
  public string Database { get; init; } = database;
  public string UserId { get; init; } = userId;
  public string Password { get; init; } = password;
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
      logger.LogError(e, "MySQL load failed for database {Database} on {Server}:{Port}.", Database, Server, Port);
      _Songs = new();
      _Users = new();
    }
  }

  private async Task<List<string>> _GetSongs(MySqlConnection connection)
  {
    List<string> songs = new();
    using var command = new MySqlCommand("SELECT filename FROM songs", connection);
    using MySqlDataReader reader = await command.ExecuteReaderAsync();

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
    using MySqlDataReader reader = await command.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
      users.Add(new NorcusUser
      {
        Guid = reader.GetGuid(0),
        Name = reader.GetString(1),
        Email = reader.GetString(2),
        Folder = reader.GetString(3),
      });
    }
    return users;
  }
}
