namespace NorcusSheetsManager.Application.Configuration;

public class DatabaseConnection
{
  public string Server { get; set; } = "localhost";
  public ushort Port { get; set; } = 3306;
  public string Database { get; set; } = "database";
  public string UserId { get; set; } = "user";
  public string Password { get; set; } = "";
}
