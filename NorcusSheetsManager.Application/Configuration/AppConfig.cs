namespace NorcusSheetsManager.Application.Configuration;

public class AppConfig
{
  public ConverterSettings Converter { get; set; } = new();
  public DatabaseConnection DbConnection { get; set; } = new();
  public ApiServerSettings ApiServer { get; set; } = new();
}
