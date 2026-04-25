using System.ComponentModel.DataAnnotations;

namespace NorcusSheetsManager.Application.Configuration;

public class ApiServerSettings : IValidatableObject
{
  public bool RunServer { get; set; } = true;
  public string Url { get; set; } = "http://0.0.0.0:4434";
  public string JwtSigningKey { get; set; } = "";

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
