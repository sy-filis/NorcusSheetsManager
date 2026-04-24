using NorcusSheetsManager.Application.Abstractions.Models;

namespace NorcusSheetsManager.Infrastructure.NameCorrector;

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
