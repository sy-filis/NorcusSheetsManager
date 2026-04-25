namespace NorcusSheetsManager.Application.Configuration;

public class AppConfig
{
  public ConverterSettings Converter { get; init; } = new();
  public DatabaseConnection DbConnection { get; init; } = new();
  public ApiServerSettings ApiServer { get; init; } = new();
}
