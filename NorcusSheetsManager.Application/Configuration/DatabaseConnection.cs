using System.ComponentModel.DataAnnotations;

namespace NorcusSheetsManager.Application.Configuration;

public class DatabaseConnection
{
  [Required(AllowEmptyStrings = false, ErrorMessage = "Server is required.")]
  public string Server { get; set; } = "localhost";

  [Range(1, 65535, ErrorMessage = "Port must be between 1 and 65535.")]
  public ushort Port { get; set; } = 3306;

  [Required(AllowEmptyStrings = false, ErrorMessage = "Database is required.")]
  public string Database { get; set; } = "database";

  [Required(AllowEmptyStrings = false, ErrorMessage = "UserId is required.")]
  public string UserId { get; set; } = "user";

  public string Password { get; set; } = "";

  [Range(1, uint.MaxValue, ErrorMessage = "RefreshIntervalSeconds must be at least 1.")]
  public uint RefreshIntervalSeconds { get; set; } = 60;
}
