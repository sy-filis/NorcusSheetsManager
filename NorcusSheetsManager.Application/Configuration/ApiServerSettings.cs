namespace NorcusSheetsManager.Application.Configuration;

public class ApiServerSettings
{
  public bool RunServer { get; set; } = true;
  public string Url { get; set; } = "http://0.0.0.0:4434";
  public string JwtSigningKey { get; set; } = "";
}
