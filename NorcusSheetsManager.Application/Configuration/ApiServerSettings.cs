using System.ComponentModel.DataAnnotations;

namespace NorcusSheetsManager.Application.Configuration;

public class ApiServerSettings : IValidatableObject
{
  public bool RunServer { get; init; } = true;
  public string Url { get; init; } = "http://0.0.0.0:4434";
  public string JwtSigningKey { get; init; } = "";

  public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
  {
    if (RunServer && string.IsNullOrEmpty(Url))
    {
      yield return new ValidationResult(
          "Url is required when RunServer is true.",
          [nameof(Url)]);
    }
  }
}
