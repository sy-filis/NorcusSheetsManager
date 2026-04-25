using System.ComponentModel.DataAnnotations;

namespace NorcusSheetsManager.Application.Configuration;

public class DatabaseConnection
{
  [Required(AllowEmptyStrings = false, ErrorMessage = "Server is required.")]
  public string Server { get; init; } = "localhost";

  [Range(1, 65535, ErrorMessage = "Port must be between 1 and 65535.")]
  public ushort Port { get; init; } = 3306;

  [Required(AllowEmptyStrings = false, ErrorMessage = "Database is required.")]
  public string Database { get; init; } = "database";

  [Required(AllowEmptyStrings = false, ErrorMessage = "UserId is required.")]
  public string UserId { get; init; } = "user";

  public string Password { get; init; } = "";

  [Range(1, uint.MaxValue, ErrorMessage = "RefreshIntervalSeconds must be at least 1.")]
  public uint RefreshIntervalSeconds { get; init; } = 60;
}
